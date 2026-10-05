using System;
using System.Collections.Generic;

namespace Apilow.GameCoin;

/// <summary>Fields of a player profile, to name which ones <see cref="PlayerProfile.Clear"/> erases.</summary>
[Flags]
public enum PlayerProfileFields
{
    /// <summary>No field.</summary>
    None = 0,

    /// <summary>The display name.</summary>
    DisplayName = 1,

    /// <summary>The e-mail address.</summary>
    Email = 2,

    /// <summary>The country.</summary>
    Country = 4,

    /// <summary>The birth year.</summary>
    BirthYear = 8,
}

/// <summary>
/// The fields <see cref="PlayersResource.UpsertAsync"/> can set. A <c>null</c> property leaves the field unchanged; to erase a
/// field, list it in <see cref="Clear"/>.
/// </summary>
public sealed record PlayerProfile
{
    /// <summary>Display name (200 characters at most).</summary>
    public string? DisplayName { get; init; }

    /// <summary>E-mail address.</summary>
    public string? Email { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country.</summary>
    public string? Country { get; init; }

    /// <summary>Birth year.</summary>
    public int? BirthYear { get; init; }

    /// <summary>Fields to erase (sent as <c>null</c>). A field cannot be both set and cleared.</summary>
    public PlayerProfileFields Clear { get; init; }
}

/// <summary>Common fields of <see cref="GrantRequest"/> and <see cref="SpendRequest"/>.</summary>
public abstract record WalletMovementRequest
{
    /// <summary>Currency code (<c>gems</c>). Required.</summary>
    public string Currency { get; init; } = "";

    /// <summary>Amount, a positive integer. Required.</summary>
    public long Amount { get; init; }

    /// <summary>Free-form reason (500 characters at most), kept in the ledger.</summary>
    public string? Reason { get; init; }

    /// <summary>Free-form string metadata, kept in the ledger.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Credits a currency to a player. A grant always credits the bonus bucket.</summary>
public sealed record GrantRequest : WalletMovementRequest;

/// <summary>Debits a currency from a player.</summary>
public sealed record SpendRequest : WalletMovementRequest;

/// <summary>Common fields of the requests that name an item and a quantity.</summary>
public abstract record ItemQuantityRequest
{
    /// <summary>Item sku. Required.</summary>
    public string Sku { get; init; } = "";

    /// <summary>Quantity, a positive integer. Default 1.</summary>
    public long Quantity { get; init; } = 1;
}

/// <summary>Puts an item in a player's inventory, for free.</summary>
public sealed record InventoryGrantRequest : ItemQuantityRequest;

/// <summary>Uses up an item of a player's inventory.</summary>
public sealed record InventoryConsumeRequest : ItemQuantityRequest;

/// <summary>Buys an item with its currency price.</summary>
public sealed record PurchaseRequest : ItemQuantityRequest;

/// <summary>Starts the purchase of a pack with real money.</summary>
public sealed record CheckoutRequest
{
    /// <summary>Pack sku. Required.</summary>
    public string PackSku { get; init; } = "";

    /// <summary>Where the player goes after paying (<c>?order=…&amp;status=fulfilled</c> is appended).</summary>
    public string? SuccessUrl { get; init; }

    /// <summary>Where the player goes when giving up.</summary>
    public string? CancelUrl { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country of the player, for the VAT.</summary>
    public string? Country { get; init; }
}

/// <summary>Refunds an order.</summary>
public sealed record RefundRequest
{
    /// <summary>Free-form reason (500 characters at most).</summary>
    public string? Reason { get; init; }
}

/// <summary>Filters and paging of a ledger listing.</summary>
public sealed record LedgerQuery
{
    /// <summary>Only the entries of this currency.</summary>
    public string? Currency { get; init; }

    /// <summary>Page size, 1 to 100 (the API default is 20).</summary>
    public int? Limit { get; init; }

    /// <summary>Cursor of the page to fetch (<see cref="LedgerPage.NextCursor"/> of the previous page).</summary>
    public string? Cursor { get; init; }
}
