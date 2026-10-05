using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Apilow.GameCoin;

/// <summary>
/// The only exception the SDK raises for a failed call (arguments rejected locally raise the usual
/// <see cref="ArgumentException"/> instead, and a cancelled call raises <see cref="OperationCanceledException"/>).
/// The secret key never appears in it.
/// </summary>
public sealed class GameCoinException : Exception
{
    private static readonly IReadOnlyDictionary<string, JsonElement> NoDetails = new Dictionary<string, JsonElement>(0);

    /// <summary>Creates an error. Mostly useful in tests; the SDK creates its own.</summary>
    /// <param name="code">API code or SDK code, see <see cref="GameCoinErrorCodes"/>.</param>
    /// <param name="status">HTTP status, 0 when no response was received.</param>
    /// <param name="message">Message of the API, or a short description.</param>
    /// <param name="details"><c>details</c> object of the API, when present.</param>
    /// <param name="retryAfter">Value of the <c>Retry-After</c> header, when present.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public GameCoinException(
        string code,
        int status,
        string message,
        IReadOnlyDictionary<string, JsonElement>? details = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Status = status;
        Details = details ?? NoDetails;
        RetryAfter = retryAfter;
    }

    /// <summary>API code (<c>INSUFFICIENT_FUNDS</c>, <c>NOT_FOUND</c>...) or SDK code (<c>NETWORK_ERROR</c>, <c>TIMEOUT</c>, <c>INVALID_RESPONSE</c>). See <see cref="GameCoinErrorCodes"/>.</summary>
    public string Code { get; }

    /// <summary>HTTP status, 0 when no response was received.</summary>
    public int Status { get; }

    /// <summary>
    /// <c>details</c> object of the API (<c>required</c> and <c>available</c> for <c>INSUFFICIENT_FUNDS</c>,
    /// <c>fieldErrors</c> for <c>VALIDATION_FAILED</c>...); empty when absent. Meant for developers.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> Details { get; }

    /// <summary>The <c>Retry-After</c> header of the response, when the API sent one.</summary>
    public TimeSpan? RetryAfter { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        var head = $"{nameof(GameCoinException)}: [{Code}] (HTTP {Status}) {Message}";
        return StackTrace is null ? head : head + Environment.NewLine + StackTrace;
    }
}
