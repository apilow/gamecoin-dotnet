using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Apilow.GameCoin.Internal;

internal enum RetryPolicy
{
    /// <summary>Network error, timeout, 5xx and 429.</summary>
    All,

    /// <summary>429 only (calls that carry no idempotency key: a blind retry could repeat the action).</summary>
    RateLimitOnly,
}

internal readonly struct ApiResponse<T>
{
    public ApiResponse(T value, bool replayed)
    {
        Value = value;
        Replayed = replayed;
    }

    public T Value { get; }

    public bool Replayed { get; }
}

internal readonly struct RawResponse
{
    public RawResponse(int status, byte[] body, bool replayed, TimeSpan? retryAfter)
    {
        Status = status;
        Body = body;
        Replayed = replayed;
        RetryAfter = retryAfter;
    }

    public int Status { get; }

    public byte[] Body { get; }

    public bool Replayed { get; }

    public TimeSpan? RetryAfter { get; }

    public bool IsSuccess => Status is >= 200 and < 300;
}

/// <summary>HTTP transport shared by every resource: headers, one URL encoding, retries, idempotency, error mapping.</summary>
internal sealed class ApiClient
{
    internal const string ApiPath = "/api/v1";

    /// <summary>A Retry-After longer than this is not waited for: the error is raised instead.</summary>
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    private static readonly object JitterLock = new();
    private static readonly Random JitterSource = new();

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _secretKey;
    private readonly TimeSpan _timeout;
    private readonly int _maxRetries;
    private readonly string _userAgent;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<double> _jitter;

    public ApiClient(
        HttpClient http,
        string baseUrl,
        string secretKey,
        TimeSpan timeout,
        int maxRetries,
        string userAgent,
        Func<TimeSpan, CancellationToken, Task>? delay,
        Func<double>? jitter)
    {
        _http = http;
        _baseUrl = baseUrl;
        _secretKey = secretKey;
        _timeout = timeout;
        _maxRetries = maxRetries;
        _userAgent = userAgent;
        _delay = delay ?? Task.Delay;
        _jitter = jitter ?? NextJitter;
    }

    public static string NewIdempotencyKey() => Guid.NewGuid().ToString("D");

    /// <summary>The caller's key (validated), or a fresh UUID v4 that every retry of the call will reuse.</summary>
    public static string ResolveIdempotencyKey(RequestOptions? options) =>
        Guard.IdempotencyKey(options?.IdempotencyKey) ?? NewIdempotencyKey();

    public async Task<ApiResponse<T>> SendAsync<T>(
        HttpMethod method,
        string path,
        ResponseSpec<T> response,
        CancellationToken cancellationToken,
        string? query = null,
        byte[]? body = null,
        string? idempotencyKey = null,
        RetryPolicy retryPolicy = RetryPolicy.All)
    {
        var uri = new Uri(_baseUrl + ApiPath + path + query, UriKind.Absolute);

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GameCoinException failure;
            try
            {
                var raw = await SendOnceAsync(method, uri, body, idempotencyKey, cancellationToken).ConfigureAwait(false);
                if (raw.IsSuccess)
                {
                    return Parse(raw, response);
                }

                failure = ErrorFromResponse(raw);
            }
            catch (GameCoinException ex) when (ex.Status == 0)
            {
                failure = ex; // NETWORK_ERROR or TIMEOUT
            }

            if (attempt >= _maxRetries || !IsRetryable(failure, retryPolicy) || RetryDelay(failure, attempt) is not { } wait)
            {
                throw failure;
            }

            await _delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsRetryable(GameCoinException failure, RetryPolicy policy)
    {
        if (failure.Status == 429)
        {
            return true;
        }

        return policy == RetryPolicy.All && (failure.Status == 0 || failure.Status >= 500);
    }

    /// <summary><c>null</c> when the server asks for a wait beyond the cap: do not retry.</summary>
    private TimeSpan? RetryDelay(GameCoinException failure, int attempt)
    {
        if (failure.RetryAfter is { } retryAfter)
        {
            return retryAfter > MaxRetryDelay ? null : retryAfter;
        }

        var seconds = 0.5 * Math.Pow(2, attempt) * (0.75 + 0.5 * _jitter()); // 0.5 s x 2^attempt, +/- 25 % jitter
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxRetryDelay.TotalSeconds));
    }

    private static double NextJitter()
    {
        lock (JitterLock)
        {
            return JitterSource.NextDouble();
        }
    }

    private async Task<RawResponse> SendOnceAsync(
        HttpMethod method,
        Uri uri,
        byte[]? body,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        using var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _secretKey);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }

        try
        {
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
#if NET5_0_OR_GREATER
            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
#else
            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
#endif
            return new RawResponse((int)response.StatusCode, bytes, IsReplay(response), ParseRetryAfter(response));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GameCoinException(
                GameCoinErrorCodes.Timeout,
                0,
                $"No response within {_timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} s.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new GameCoinException(GameCoinErrorCodes.NetworkError, 0, Scrub("Network error: " + ex.Message), innerException: ex);
        }
    }

    private ApiResponse<T> Parse<T>(RawResponse raw, ResponseSpec<T> response)
    {
        GameCoinException Invalid(string reason, Exception? cause = null) =>
            new(GameCoinErrorCodes.InvalidResponse, raw.Status, $"The response of the API (HTTP {raw.Status}) is not the expected JSON: {reason}.", innerException: cause);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw.Body);
        }
        catch (JsonException ex)
        {
            throw Invalid("the body is not JSON", ex);
        }

        using (document)
        {
            try
            {
                response.Check(document.RootElement);
                var value = document.RootElement.Deserialize(response.TypeInfo);
                if (value is null)
                {
                    throw Invalid("the body is empty");
                }

                return new ApiResponse<T>(value, raw.Replayed);
            }
            catch (ShapeException ex)
            {
                throw Invalid(ex.Message);
            }
            catch (JsonException ex)
            {
                throw Invalid("a field has the wrong type", ex);
            }
        }
    }

    private GameCoinException ErrorFromResponse(RawResponse raw)
    {
        if (TryReadErrorBody(raw.Body, out var code, out var message, out var details))
        {
            return new GameCoinException(code, raw.Status, Scrub(message), details, raw.RetryAfter);
        }

        var description = raw.Status is >= 300 and < 400
            ? $"Unexpected redirect (HTTP {raw.Status}): check the base URL (scheme and host)."
            : $"Unexpected response (HTTP {raw.Status}): the body is not a GameCoin error.";
        return new GameCoinException(GameCoinErrorCodes.InvalidResponse, raw.Status, description, retryAfter: raw.RetryAfter);
    }

    private static bool TryReadErrorBody(
        byte[] body,
        out string code,
        out string message,
        out IReadOnlyDictionary<string, JsonElement>? details)
    {
        code = string.Empty;
        message = string.Empty;
        details = null;
        if (body.Length == 0)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("code", out var codeElement)
                || codeElement.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(codeElement.GetString()))
            {
                return false;
            }

            code = codeElement.GetString()!;
            if (!error.TryGetProperty("message", out var messageElement) || messageElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            message = messageElement.GetString() ?? string.Empty;

            if (error.TryGetProperty("details", out var detailsElement) && detailsElement.ValueKind == JsonValueKind.Object)
            {
                var copy = new Dictionary<string, JsonElement>();
                foreach (var property in detailsElement.EnumerateObject())
                {
                    copy[property.Name] = property.Value.Clone();
                }

                details = copy;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsReplay(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Idempotent-Replayed", out var values)
        && values.Any(v => string.Equals(v.Trim(), "true", StringComparison.OrdinalIgnoreCase));

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values))
        {
            return null;
        }

        var raw = values.FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return double.IsNaN(seconds) || seconds < 0 ? null : TimeSpan.FromSeconds(Math.Min(seconds, 31_536_000));
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }

    /// <summary>Defence in depth: whatever the server or the network stack says, the secret key never leaves in a message.</summary>
    private string Scrub(string text) => text.Replace(_secretKey, "[redacted]");
}
