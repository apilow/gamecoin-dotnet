using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Apilow.GameCoin;

/// <summary>Options of a <see cref="GameCoinClient"/>. Every option is optional.</summary>
public sealed class GameCoinOptions
{
    /// <summary>
    /// Base URL of GameCoin: the origin of the service, to which the SDK appends <c>/api/v1</c>. Trailing slashes are dropped and
    /// a path prefix is kept (for a gateway); anything but an http(s) URL without credentials, query or fragment is refused.
    /// Default <see cref="GameCoinClient.DefaultBaseUrl"/>.
    /// </summary>
    public string BaseUrl { get; init; } = GameCoinClient.DefaultBaseUrl;

    /// <summary>Timeout of each HTTP attempt (a retry gets a fresh timeout). Default 30 seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Automatic retries after a network error, a timeout, a <c>5xx</c> or a <c>429</c> (so the default of 2 means up to
    /// three attempts). 0 disables retries; the maximum is 10. See the README, "Idempotency and retries".
    /// </summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>
    /// Your own <see cref="System.Net.Http.HttpClient"/> (for instance one from <c>IHttpClientFactory</c>). The SDK never
    /// changes its default headers and never disposes it. Mutually exclusive with <see cref="HttpMessageHandler"/>.
    /// </summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>
    /// A message handler the SDK wraps in its own <see cref="System.Net.Http.HttpClient"/> (tests, proxies). The SDK does not
    /// dispose the handler. Mutually exclusive with <see cref="HttpClient"/>.
    /// </summary>
    public HttpMessageHandler? HttpMessageHandler { get; init; }

    // Test hooks (InternalsVisibleTo): no real waiting, deterministic jitter.
    internal Func<TimeSpan, CancellationToken, Task>? DelayAsync { get; init; }

    internal Func<double>? Jitter { get; init; }
}

/// <summary>Per-call options of the calls that change something (marked "idempotent" in their documentation).</summary>
public sealed record RequestOptions
{
    /// <summary>
    /// Your own <c>Idempotency-Key</c> (1 to 100 printable ASCII characters), to make a retry of your own safe. Reusing a key
    /// with the same request replays the original response (<c>Replayed</c> is then <c>true</c>); reusing it with another
    /// request raises <see cref="GameCoinErrorCodes.IdempotencyConflict"/>. When <c>null</c>, the SDK generates a UUID v4 once
    /// per call and reuses it for every automatic retry of that call.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}
