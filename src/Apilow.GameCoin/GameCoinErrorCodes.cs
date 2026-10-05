namespace Apilow.GameCoin;

/// <summary>
/// The values of <see cref="GameCoinException.Code"/>: the error codes of the GameCoin API, plus three codes raised by
/// the SDK itself (<see cref="NetworkError"/>, <see cref="Timeout"/>, <see cref="InvalidResponse"/>).
/// </summary>
/// <example>
/// <code>
/// catch (GameCoinException ex) when (ex.Code == GameCoinErrorCodes.InsufficientFunds) { /* ... */ }
/// </code>
/// </example>
public static class GameCoinErrorCodes
{
    /// <summary>400: body or query invalid (see <c>Details["fieldErrors"]</c>).</summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>400: missing or empty <c>Idempotency-Key</c> on a mutation. The SDK always sends one.</summary>
    public const string IdempotencyKeyRequired = "IDEMPOTENCY_KEY_REQUIRED";

    /// <summary>401: missing, unknown or revoked key; invalid or expired player token.</summary>
    public const string Unauthenticated = "UNAUTHENTICATED";

    /// <summary>402: payment refused, or no payment provider for this environment.</summary>
    public const string PaymentFailed = "PAYMENT_FAILED";

    /// <summary>403: key kind not allowed here, anonymous players disabled, or item not client-purchasable.</summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>403: the player is blocked.</summary>
    public const string PlayerBlocked = "PLAYER_BLOCKED";

    /// <summary>403: the <c>live</c> environment was used by a studio that is not verified.</summary>
    public const string EnvNotEnabled = "ENV_NOT_ENABLED";

    /// <summary>404: unknown player, currency, item, pack or order in this game and environment.</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>409: the wallet (or the item quantity) is too low (<c>Details["required"]</c>, <c>Details["available"]</c>).</summary>
    public const string InsufficientFunds = "INSUFFICIENT_FUNDS";

    /// <summary>409: <c>maxOwned</c> or <c>maxPerPlayer</c> exceeded.</summary>
    public const string LimitReached = "LIMIT_REACHED";

    /// <summary>409: the idempotency key was already used with another request.</summary>
    public const string IdempotencyConflict = "IDEMPOTENCY_CONFLICT";

    /// <summary>409: invalid state transition (for example the refund of an unpaid order).</summary>
    public const string Conflict = "CONFLICT";

    /// <summary>423: the studio is suspended and its space is read-only.</summary>
    public const string ReadOnly = "READ_ONLY";

    /// <summary>429: too many requests (see <see cref="GameCoinException.RetryAfter"/>).</summary>
    public const string RateLimited = "RATE_LIMITED";

    /// <summary>500: unexpected error on the GameCoin side.</summary>
    public const string Internal = "INTERNAL";

    /// <summary>SDK: no response was received (DNS, connection refused, reset...). <see cref="GameCoinException.Status"/> is 0.</summary>
    public const string NetworkError = "NETWORK_ERROR";

    /// <summary>SDK: no response within <see cref="GameCoinOptions.Timeout"/>. <see cref="GameCoinException.Status"/> is 0.</summary>
    public const string Timeout = "TIMEOUT";

    /// <summary>SDK: the body of the response is not the expected JSON.</summary>
    public const string InvalidResponse = "INVALID_RESPONSE";
}
