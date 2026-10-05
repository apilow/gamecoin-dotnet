using System;

namespace Apilow.GameCoin.Internal;

/// <summary>
/// Local argument checks: a bad argument never costs a request. A message names the argument, never its value (a value could
/// be a secret).
/// </summary>
internal static class Guard
{
    public static void NotNull(object? value, string paramName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }
    }

    /// <summary>A string that must not be empty.</summary>
    public static string NotEmpty(string? value, string what, string paramName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException($"{what} must not be empty.", paramName);
        }

        return value!;
    }

    /// <summary>An optional string: <c>null</c> is "not given", but an empty string is a mistake.</summary>
    public static string? NotEmptyIfGiven(string? value, string what, string paramName) =>
        value is null ? null : NotEmpty(value, what, paramName);

    /// <summary>Validates a player reference and returns it URL-encoded, ready to be a path segment.</summary>
    public static string PlayerSegment(PlayerRef player)
    {
        if (player.IsEmpty)
        {
            throw new ArgumentException("The player must not be empty: use PlayerRef.Ext(...) or PlayerRef.FromId(...).", nameof(player));
        }

        return Uri.EscapeDataString(player.Value);
    }

    /// <summary>An integer from <paramref name="min"/> to <paramref name="max"/>.</summary>
    public static void InRange(long value, long min, long max, string what, string paramName)
    {
        if (value < min || value > max)
        {
            var range = max == long.MaxValue ? $"at least {min}" : $"from {min} to {max}";
            throw new ArgumentOutOfRangeException(paramName, $"{what} must be an integer {range}.");
        }
    }

    public static void Positive(long value, string what, string paramName) => InRange(value, 1, long.MaxValue, what, paramName);

    /// <summary>1 to 100 printable ASCII characters, as the API requires.</summary>
    public static string? IdempotencyKey(string? key)
    {
        if (key is null)
        {
            return null;
        }

        if (key.Length is < 1 or > 100)
        {
            throw new ArgumentException("The idempotency key must be 1 to 100 characters long.", nameof(RequestOptions.IdempotencyKey));
        }

        foreach (var c in key)
        {
            if (c < 0x20 || c > 0x7E)
            {
                throw new ArgumentException("The idempotency key must only contain printable ASCII characters.", nameof(RequestOptions.IdempotencyKey));
            }
        }

        return key;
    }
}
