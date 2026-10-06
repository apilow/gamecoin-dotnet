using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Apilow.GameCoin.Internal;

namespace Apilow.GameCoin;

/// <summary>Options of <see cref="Webhooks.ConstructEvent(string, string, string, WebhookOptions)"/>.</summary>
public sealed record WebhookOptions
{
    /// <summary>Default of <see cref="ToleranceSeconds"/>: 5 minutes.</summary>
    public const int DefaultToleranceSeconds = 300;

    /// <summary>
    /// How far, in seconds and in both directions, the timestamp of the signature may be from the clock. It protects you from a
    /// replayed call. Default 300; <c>0</c> turns the timestamp check off.
    /// </summary>
    public int ToleranceSeconds { get; init; } = DefaultToleranceSeconds;

    /// <summary>The clock, for tests. Default: the system clock.</summary>
    public Func<DateTimeOffset>? Now { get; init; }
}

/// <summary>
/// Verification of the webhooks GameCoin sends to your server. Pure functions: no client, no network.
/// </summary>
/// <remarks>
/// Pass the <b>raw</b> body of the request, exactly as received, <b>before</b> any JSON parsing: a body that was parsed and
/// serialized again can differ by one character, and the signature then fails. In ASP.NET Core copy <c>Request.Body</c> to a
/// <see cref="System.IO.MemoryStream"/> and pass its bytes (see the README).
/// </remarks>
public static class Webhooks
{
    private const string SignatureHeaderName = "GameCoin-Signature";

    /// <summary>
    /// Checks the signature of a webhook call and returns its event. The signature is the lowercase hexadecimal HMAC-SHA256 of
    /// <c>&lt;t&gt;.&lt;raw body&gt;</c> keyed with the signing secret of the endpoint (<c>whsec_gc_…</c>); every <c>v1</c> of the
    /// header is compared in constant time, and unknown keys of the header are ignored.
    /// </summary>
    /// <param name="rawBody">The raw body as text (UTF-8 text, as read from the request).</param>
    /// <param name="signatureHeader">Value of the <c>GameCoin-Signature</c> header: <c>t=&lt;unix seconds&gt;,v1=&lt;hex&gt;</c>.</param>
    /// <param name="secret">The signing secret of the endpoint; surrounding white space is trimmed.</param>
    /// <param name="options">Tolerance and clock; <c>null</c> for the defaults.</param>
    /// <returns>The verified, typed event.</returns>
    /// <exception cref="ArgumentException">The body is null or the secret is empty (checked locally, never echoed).</exception>
    /// <exception cref="WebhookSignatureException">The call is not trustworthy or not an event, see <see cref="WebhookSignatureReason"/>.</exception>
    public static WebhookEvent ConstructEvent(string rawBody, string? signatureHeader, string secret, WebhookOptions? options = null)
    {
        if (rawBody is null)
        {
            throw new ArgumentNullException(nameof(rawBody), "Pass the raw body of the request.");
        }

        return ConstructEvent(Encoding.UTF8.GetBytes(rawBody), signatureHeader, secret, options);
    }

    /// <summary>
    /// Checks the signature of a webhook call and returns its event. Same as the overload that takes text, for a body read as
    /// bytes.
    /// </summary>
    /// <param name="rawBody">The raw body, exactly as received.</param>
    /// <param name="signatureHeader">Value of the <c>GameCoin-Signature</c> header.</param>
    /// <param name="secret">The signing secret of the endpoint; surrounding white space is trimmed.</param>
    /// <param name="options">Tolerance and clock; <c>null</c> for the defaults.</param>
    /// <returns>The verified, typed event.</returns>
    /// <exception cref="ArgumentException">The body is null or the secret is empty (checked locally, never echoed).</exception>
    /// <exception cref="WebhookSignatureException">The call is not trustworthy or not an event, see <see cref="WebhookSignatureReason"/>.</exception>
    public static WebhookEvent ConstructEvent(byte[] rawBody, string? signatureHeader, string secret, WebhookOptions? options = null)
    {
        if (rawBody is null)
        {
            throw new ArgumentNullException(nameof(rawBody), "Pass the raw body of the request.");
        }

        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("The webhook signing secret must not be empty.", nameof(secret));
        }

        options ??= new WebhookOptions();
        if (options.ToleranceSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "ToleranceSeconds must be 0 (no timestamp check) or more.");
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            throw new WebhookSignatureException(WebhookSignatureReason.MissingHeader, $"The {SignatureHeaderName} header is missing.");
        }

        var (timestamp, candidates) = ParseHeader(signatureHeader!);

        var expected = ComputeSignature(secret.Trim(), timestamp, rawBody);
        var matched = false;
        foreach (var candidate in candidates)
        {
            // No early exit: the time taken does not tell which candidate matched.
            matched |= Matches(candidate, expected);
        }

        if (!matched)
        {
            throw new WebhookSignatureException(
                WebhookSignatureReason.NoMatchingSignature,
                "No signature matches the body: check the signing secret and that the body is the raw one, unchanged.");
        }

        if (options.ToleranceSeconds > 0)
        {
            var now = (options.Now?.Invoke() ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            if (Math.Abs(now - timestamp) > options.ToleranceSeconds)
            {
                throw new WebhookSignatureException(
                    WebhookSignatureReason.TimestampOutOfTolerance,
                    $"The timestamp of the signature is more than {options.ToleranceSeconds} seconds away from the clock.");
            }
        }

        return ParseEvent(rawBody);
    }

    // --- Header -----------------------------------------------------------------------------------------------------

    private static (long Timestamp, List<string> Signatures) ParseHeader(string header)
    {
        long? timestamp = null;
        var signatures = new List<string>();
        foreach (var part in header.Split(','))
        {
            var equals = part.IndexOf('=');
            if (equals < 0)
            {
                continue;
            }

            var key = part.Substring(0, equals).Trim();
            var value = part.Substring(equals + 1).Trim();
            if (key == "t")
            {
                if (timestamp is not null || !TryParseTimestamp(value, out var parsed))
                {
                    throw Malformed();
                }

                timestamp = parsed;
            }
            else if (key == "v1" && value.Length > 0)
            {
                signatures.Add(value);
            }
        }

        if (timestamp is null || signatures.Count == 0)
        {
            throw Malformed();
        }

        return (timestamp.Value, signatures);
    }

    private static WebhookSignatureException Malformed() =>
        new(WebhookSignatureReason.MalformedHeader, $"The {SignatureHeaderName} header must look like t=<unix seconds>,v1=<hex signature>.");

    private static bool TryParseTimestamp(string value, out long timestamp)
    {
        timestamp = 0;
        if (value.Length == 0 || value.Length > 18)
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out timestamp);
    }

    // --- Signature --------------------------------------------------------------------------------------------------

    private static byte[] ComputeSignature(string secret, long timestamp, byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var prefix = Encoding.ASCII.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + ".");
        hmac.TransformBlock(prefix, 0, prefix.Length, null, 0);
        hmac.TransformFinalBlock(body, 0, body.Length);
        return hmac.Hash!;
    }

    /// <summary>Compares a hexadecimal candidate (either case) with the expected digest in constant time; not hexadecimal never matches.</summary>
    private static bool Matches(string candidate, byte[] expected)
    {
        if (candidate.Length != expected.Length * 2)
        {
            return false;
        }

        var given = new byte[expected.Length];
        var valid = true;
        for (var i = 0; i < given.Length; i++)
        {
            var high = HexValue(candidate[2 * i]);
            var low = HexValue(candidate[2 * i + 1]);
            valid &= (high | low) >= 0;
            given[i] = (byte)(((high & 0xF) << 4) | (low & 0xF));
        }

        return CryptographicOperations.FixedTimeEquals(given, expected) & valid;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    // --- Event ------------------------------------------------------------------------------------------------------

    private static WebhookEvent ParseEvent(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            Shape.WebhookEnvelope(root);

            var type = root.GetProperty("type").GetString()!;
            var data = root.GetProperty("data");
            object typed;
            switch (type)
            {
                case WebhookEventTypes.OrderFulfilled:
                case WebhookEventTypes.OrderRefunded:
                case WebhookEventTypes.OrderChargeback:
                    Shape.Order(data, "event.data");
                    typed = data.Deserialize(GameCoinJsonContext.Default.Order)!;
                    break;
                case WebhookEventTypes.WalletCredited:
                    Shape.WalletCredited(data, "event.data");
                    typed = data.Deserialize(GameCoinJsonContext.Default.WalletCreditedData)!;
                    break;
                case WebhookEventTypes.WebhookTest:
                    Shape.WebhookTest(data, "event.data");
                    typed = data.Deserialize(GameCoinJsonContext.Default.WebhookTestData)!;
                    break;
                default:
                    typed = data.Clone(); // a type added later: passed through, untyped
                    break;
            }

            return new WebhookEvent
            {
                Id = root.GetProperty("id").GetString()!,
                Type = type,
                CreatedAt = root.GetProperty("createdAt").GetDateTimeOffset(),
                Env = root.GetProperty("env").GetString()!,
                Data = typed,
            };
        }
        catch (JsonException)
        {
            throw new WebhookSignatureException(WebhookSignatureReason.InvalidPayload, "The body is not valid JSON.");
        }
        catch (ShapeException error)
        {
            throw new WebhookSignatureException(WebhookSignatureReason.InvalidPayload, "The body is not a GameCoin event: " + error.Message + ".");
        }
    }
}
