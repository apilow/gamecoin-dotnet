using System;

namespace Apilow.GameCoin;

/// <summary>Values of <see cref="WebhookSignatureException.Reason"/>.</summary>
public static class WebhookSignatureReason
{
    /// <summary>The <c>GameCoin-Signature</c> header is absent or empty.</summary>
    public const string MissingHeader = "missing_header";

    /// <summary>The header has no usable timestamp (<c>t</c>) or no <c>v1</c> signature.</summary>
    public const string MalformedHeader = "malformed_header";

    /// <summary>No <c>v1</c> signature matches the body: wrong secret, altered body, or a body that was parsed and serialized again.</summary>
    public const string NoMatchingSignature = "no_matching_signature";

    /// <summary>The signature is right but the timestamp is too far from the clock (a replayed call, or a skewed clock).</summary>
    public const string TimestampOutOfTolerance = "timestamp_out_of_tolerance";

    /// <summary>The signature is right but the body is not JSON, or not an event envelope.</summary>
    public const string InvalidPayload = "invalid_payload";
}

/// <summary>
/// Raised by <see cref="Webhooks.ConstructEvent(string, string, string, WebhookOptions)"/> when a webhook call cannot be trusted
/// or understood. It does <b>not</b> derive from <see cref="GameCoinException"/> (no API call is involved): answer <c>400</c>.
/// The message never contains the secret, the body or the signature.
/// </summary>
public sealed class WebhookSignatureException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="reason">One of the <see cref="WebhookSignatureReason"/> values.</param>
    /// <param name="message">A short description.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public WebhookSignatureException(string reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    /// <summary>Why the call was refused, see <see cref="WebhookSignatureReason"/>.</summary>
    public string Reason { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        var head = $"{nameof(WebhookSignatureException)}: [{Reason}] {Message}";
        return StackTrace is null ? head : head + Environment.NewLine + StackTrace;
    }
}
