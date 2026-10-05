using System;

namespace Apilow.GameCoin;

/// <summary>
/// Designates a player: either a GameCoin player id, or your game's own id through <see cref="Ext"/>
/// (<c>ext:user-42</c>). A GameCoin <c>ext:</c> player is created the first time a write call names it.
/// </summary>
/// <example>
/// <code>
/// await gamecoin.Wallet.GetAsync(PlayerRef.Ext("user-42"));
/// await gamecoin.Wallet.GetAsync(PlayerRef.FromId("6ac40ac02e9fe50981a1e085"));
/// </code>
/// </example>
public readonly struct PlayerRef : IEquatable<PlayerRef>
{
    private readonly string? _value;

    private PlayerRef(string value) => _value = value;

    /// <summary>Your game's own id for the player: <c>Ext("user-42")</c> is the GameCoin reference <c>ext:user-42</c>.</summary>
    /// <param name="externalId">Any non-empty string; spaces, slashes or non-ASCII characters are fine (they are URL-encoded once).</param>
    /// <exception cref="ArgumentException"><paramref name="externalId"/> is null or empty.</exception>
    public static PlayerRef Ext(string externalId)
    {
        if (string.IsNullOrEmpty(externalId))
        {
            throw new ArgumentException("The external player id must not be empty.", nameof(externalId));
        }

        return new PlayerRef("ext:" + externalId);
    }

    /// <summary>A GameCoin player id, as returned in <see cref="Player.Id"/>.</summary>
    /// <param name="playerId">The GameCoin id (an <c>ext:</c> reference is accepted too).</param>
    /// <exception cref="ArgumentException"><paramref name="playerId"/> is null or empty.</exception>
    public static PlayerRef FromId(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
        {
            throw new ArgumentException("The player id must not be empty.", nameof(playerId));
        }

        return new PlayerRef(playerId);
    }

    /// <summary>The reference as the API names it: a GameCoin id or <c>ext:&lt;your id&gt;</c>. Empty for <c>default</c>.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary><c>true</c> for <c>default(PlayerRef)</c>, which every call rejects.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(_value);

    /// <summary>A plain string is taken as a GameCoin player id; use <see cref="Ext"/> for your own ids.</summary>
    public static implicit operator PlayerRef(string playerId) => FromId(playerId);

    /// <inheritdoc />
    public bool Equals(PlayerRef other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PlayerRef other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Equality of two references.</summary>
    public static bool operator ==(PlayerRef left, PlayerRef right) => left.Equals(right);

    /// <summary>Inequality of two references.</summary>
    public static bool operator !=(PlayerRef left, PlayerRef right) => !left.Equals(right);
}
