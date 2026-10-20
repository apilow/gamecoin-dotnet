using System;
using System.Collections.Generic;

namespace Apilow.GameCoin;

// Catalog management: what CurrenciesResource, ItemsResource and PacksResource return. The catalog objects plus the status, the
// dates and, for a pack, the raw availability settings. Archived resources and draft packs are visible here.

/// <summary>Known values of <see cref="ManagedCurrency.Status"/> and <see cref="ManagedItem.Status"/>.</summary>
public static class CatalogStatus
{
    /// <summary>Offered.</summary>
    public const string Active = "active";

    /// <summary>No longer offered, creditable or buyable; balances and inventories that refer to it are untouched.</summary>
    public const string Archived = "archived";
}

/// <summary>Known values of <see cref="ManagedPack.Status"/>.</summary>
public static class PackStatus
{
    /// <summary>Not sold (the status of a new pack).</summary>
    public const string Draft = "draft";

    /// <summary>Sold.</summary>
    public const string Active = "active";

    /// <summary>No longer sold; the orders already placed are unaffected.</summary>
    public const string Archived = "archived";
}

/// <summary>A currency as <see cref="CurrenciesResource"/> manages it: the catalog fields, the status and the dates.</summary>
public sealed record ManagedCurrency
{
    /// <summary>Currency code.</summary>
    public string Code { get; init; } = "";

    /// <summary>Display name.</summary>
    public string Name { get; init; } = "";

    /// <summary><c>true</c> when packs can sell it for real money.</summary>
    public bool Purchasable { get; init; }

    /// <summary><see cref="CatalogStatus.Active"/> or <see cref="CatalogStatus.Archived"/>.</summary>
    public string Status { get; init; } = "";

    /// <summary>When the currency was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the currency was last changed.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>An item as <see cref="ItemsResource"/> manages it: the catalog fields, the status and the dates.</summary>
public sealed record ManagedItem
{
    /// <summary>Item sku.</summary>
    public string Sku { get; init; } = "";

    /// <summary>Display name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Description, if any.</summary>
    public string? Description { get; init; }

    /// <summary><c>consumable</c> or <c>durable</c>, see <see cref="ItemType"/>.</summary>
    public string Type { get; init; } = "";

    /// <summary>Price in a currency, <c>null</c> when the item cannot be bought with a currency.</summary>
    public ItemPrice? Price { get; init; }

    /// <summary>Maximum quantity a player can own, <c>null</c> for no limit.</summary>
    public long? MaxOwned { get; init; }

    /// <summary>Whether the browser SDK may buy it.</summary>
    public bool ClientPurchasable { get; init; }

    /// <summary>Free-form metadata of the item (empty when none).</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = Empty.Metadata;

    /// <summary><see cref="CatalogStatus.Active"/> or <see cref="CatalogStatus.Archived"/>.</summary>
    public string Status { get; init; } = "";

    /// <summary>When the item was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the item was last changed.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// The sale settings of a pack as they were written, with the units delivered so far. The public catalog shows the remaining
/// stock instead (<see cref="PackAvailability"/>).
/// </summary>
public sealed record ManagedPackAvailability
{
    /// <summary>First instant of sale (included), <c>null</c> for on sale from the start.</summary>
    public DateTimeOffset? StartsAt { get; init; }

    /// <summary>First instant when the pack is no longer sold (excluded), <c>null</c> for no end.</summary>
    public DateTimeOffset? EndsAt { get; init; }

    /// <summary>Total number of units that can be sold, <c>null</c> for an unlimited stock.</summary>
    public long? Stock { get; init; }

    /// <summary><c>true</c> for a founder pack.</summary>
    public bool Founder { get; init; }

    /// <summary>Units delivered so far.</summary>
    public long Sold { get; init; }
}

/// <summary>
/// A pack as <see cref="PacksResource"/> manages it: the fields of <see cref="CatalogPack"/> (<c>Availability</c> aside), the
/// display order, the status and the dates.
/// </summary>
public sealed record ManagedPack
{
    /// <summary>Pack sku.</summary>
    public string Sku { get; init; } = "";

    /// <summary>Display name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Description, if any.</summary>
    public string? Description { get; init; }

    /// <summary>Price in cents (VAT included).</summary>
    public long PriceCents { get; init; }

    /// <summary>Always <c>EUR</c>.</summary>
    public string Currency { get; init; } = "";

    /// <summary>Currency amounts credited.</summary>
    public IReadOnlyList<PackGrant> Grants { get; init; } = Array.Empty<PackGrant>();

    /// <summary>Items delivered.</summary>
    public IReadOnlyList<PackItem> Items { get; init; } = Array.Empty<PackItem>();

    /// <summary><c>popular</c> or <c>best_value</c>, if any.</summary>
    public string? Badge { get; init; }

    /// <summary>Maximum number of purchases per player, <c>null</c> for no limit.</summary>
    public long? MaxPerPlayer { get; init; }

    /// <summary>Display order in the shop, ascending.</summary>
    public long SortOrder { get; init; }

    /// <summary><see cref="PackStatus.Draft"/>, <see cref="PackStatus.Active"/> or <see cref="PackStatus.Archived"/>.</summary>
    public string Status { get; init; } = "";

    /// <summary>Sale settings; <c>null</c> for an ordinary pack (always on sale, no limit).</summary>
    public ManagedPackAvailability? Availability { get; init; }

    /// <summary>When the pack was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the pack was last changed.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}
