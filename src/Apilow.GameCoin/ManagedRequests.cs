using System;
using System.Collections.Generic;

namespace Apilow.GameCoin;

// Requests of the catalog management. The catalog is shared by the test and live environments of the game: a resource created
// with a test key exists for live keys too. A code or a sku is the natural key of a create (a taken one is
// GameCoinErrorCodes.Conflict with fieldErrors "sku": ["SKU_TAKEN"] or "code": ["CODE_TAKEN"]).

/// <summary>Creates a currency (<see cref="CurrenciesResource.CreateAsync"/>).</summary>
public sealed record CreateCurrencyRequest
{
    /// <summary>A lowercase letter, then lowercase letters, digits or <c>_</c> (2 to 24 characters). Unique in the game and final. Required.</summary>
    public string Code { get; init; } = "";

    /// <summary>Display name. Required.</summary>
    public string Name { get; init; } = "";

    /// <summary><c>null</c> or <c>true</c>: packs can sell it for real money; <c>false</c>: it is only given (always credited to the bonus bucket).</summary>
    public bool? Purchasable { get; init; }
}

/// <summary>Changes a currency (<see cref="CurrenciesResource.UpdateAsync"/>). A <c>null</c> property is left unchanged.</summary>
public sealed record UpdateCurrencyRequest
{
    /// <summary>New display name.</summary>
    public string? Name { get; init; }

    /// <summary>Cannot become <c>false</c> while a pack that is not archived sells the currency for money.</summary>
    public bool? Purchasable { get; init; }
}

/// <summary>Fields of an item, to name which ones <see cref="UpdateItemRequest.Clear"/> erases.</summary>
[Flags]
public enum ItemFields
{
    /// <summary>No field.</summary>
    None = 0,

    /// <summary>The description.</summary>
    Description = 1,

    /// <summary>The price: the item can no longer be bought with a currency.</summary>
    Price = 2,

    /// <summary>The limit per player: no limit any more.</summary>
    MaxOwned = 4,
}

/// <summary>Creates an item (<see cref="ItemsResource.CreateAsync"/>).</summary>
public sealed record CreateItemRequest
{
    /// <summary>Unique among the items and final. Required.</summary>
    public string Sku { get; init; } = "";

    /// <summary>Display name. Required.</summary>
    public string Name { get; init; } = "";

    /// <summary><see cref="ItemType.Consumable"/> or <see cref="ItemType.Durable"/>, and final. Required.</summary>
    public string Type { get; init; } = "";

    /// <summary>Optional text (500 characters at most).</summary>
    public string? Description { get; init; }

    /// <summary>Price in an active currency of the game; <c>null</c> for an item that cannot be bought with a currency.</summary>
    public ItemPrice? Price { get; init; }

    /// <summary>Most units a player can own. <c>null</c> means the default: 1 for a durable item, no limit for a consumable.</summary>
    public long? MaxOwned { get; init; }

    /// <summary><c>true</c> for no limit, whatever the type (<see cref="MaxOwned"/> must then be <c>null</c>).</summary>
    public bool NoMaxOwned { get; init; }

    /// <summary>Lets the browser SDK sell the item; it needs a <see cref="Price"/>.</summary>
    public bool ClientPurchasable { get; init; }

    /// <summary>Free-form string metadata that your game reads.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Changes an item (<see cref="ItemsResource.UpdateAsync"/>). A <c>null</c> property is left unchanged; to erase the description,
/// the price or the limit, list it in <see cref="Clear"/>. A field cannot be both set and cleared.
/// </summary>
public sealed record UpdateItemRequest
{
    /// <summary>New display name.</summary>
    public string? Name { get; init; }

    /// <summary>New description.</summary>
    public string? Description { get; init; }

    /// <summary>New price.</summary>
    public ItemPrice? Price { get; init; }

    /// <summary>New limit per player.</summary>
    public long? MaxOwned { get; init; }

    /// <summary>Lets the browser SDK sell the item (it needs a price).</summary>
    public bool? ClientPurchasable { get; init; }

    /// <summary>Replaces the whole metadata; an empty dictionary erases it.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>Fields to erase (sent as <c>null</c>).</summary>
    public ItemFields Clear { get; init; }
}

/// <summary>Fields of a pack, to name which ones <see cref="UpdatePackRequest.Clear"/> erases.</summary>
[Flags]
public enum PackFields
{
    /// <summary>No field.</summary>
    None = 0,

    /// <summary>The description.</summary>
    Description = 1,

    /// <summary>The badge.</summary>
    Badge = 2,

    /// <summary>The limit per player: no limit any more.</summary>
    MaxPerPlayer = 4,
}

/// <summary>
/// The whole sale setting of a pack: it replaces the previous one, it is not merged with it. A default instance is no limit at
/// all: no window, an unlimited stock and not a founder pack.
/// </summary>
public sealed record PackAvailabilityRequest
{
    /// <summary>First instant of sale (included); <c>null</c> for on sale from the start.</summary>
    public DateTimeOffset? StartsAt { get; init; }

    /// <summary>First instant when the pack is no longer sold (excluded); <c>null</c> for no end.</summary>
    public DateTimeOffset? EndsAt { get; init; }

    /// <summary>Total number of units that can be sold (1 to 1,000,000); <c>null</c> for an unlimited stock. It cannot go below the units already sold.</summary>
    public long? Stock { get; init; }

    /// <summary><c>true</c> for a founder pack.</summary>
    public bool Founder { get; init; }
}

/// <summary>Creates a pack (<see cref="PacksResource.CreateAsync"/>), as a draft unless <see cref="Status"/> says otherwise.</summary>
public sealed record CreatePackRequest
{
    /// <summary>Unique among the packs and final. Required.</summary>
    public string Sku { get; init; } = "";

    /// <summary>Display name. Required.</summary>
    public string Name { get; init; } = "";

    /// <summary>Price in euro cents, VAT included (50 to 50,000). Required.</summary>
    public long PriceCents { get; init; }

    /// <summary>Optional text (500 characters at most).</summary>
    public string? Description { get; init; }

    /// <summary>Currencies credited when the order is paid. A pack needs at least one grant or one item.</summary>
    public IReadOnlyList<PackGrant>? Grants { get; init; }

    /// <summary>Items delivered when the order is paid.</summary>
    public IReadOnlyList<PackItem>? Items { get; init; }

    /// <summary><c>popular</c> or <c>best_value</c>; <c>null</c> for none.</summary>
    public string? Badge { get; init; }

    /// <summary>Most orders per player (a starter pack: 1); <c>null</c> for no limit.</summary>
    public long? MaxPerPlayer { get; init; }

    /// <summary>Display order in the shop, ascending (-10,000 to 10,000); <c>null</c> for 0.</summary>
    public long? SortOrder { get; init; }

    /// <summary>Sale window, stock and founder flag; <c>null</c> for an ordinary pack.</summary>
    public PackAvailabilityRequest? Availability { get; init; }

    /// <summary><see cref="PackStatus.Draft"/> (the default when <c>null</c>) or <see cref="PackStatus.Active"/> to sell the pack at once.</summary>
    public string? Status { get; init; }
}

/// <summary>
/// Changes a pack (<see cref="PacksResource.UpdateAsync"/>). A <c>null</c> property is left unchanged; to erase the description, the
/// badge or the limit, list it in <see cref="Clear"/>. A field cannot be both set and cleared.
/// </summary>
public sealed record UpdatePackRequest
{
    /// <summary>New display name.</summary>
    public string? Name { get; init; }

    /// <summary>New description.</summary>
    public string? Description { get; init; }

    /// <summary>New price in euro cents. The orders already placed keep the price they were made at.</summary>
    public long? PriceCents { get; init; }

    /// <summary>Replaces the whole list of grants; <c>null</c> leaves it unchanged, an empty list empties it.</summary>
    public IReadOnlyList<PackGrant>? Grants { get; init; }

    /// <summary>Replaces the whole list of items; <c>null</c> leaves it unchanged, an empty list empties it.</summary>
    public IReadOnlyList<PackItem>? Items { get; init; }

    /// <summary><c>popular</c> or <c>best_value</c>.</summary>
    public string? Badge { get; init; }

    /// <summary>New limit per player.</summary>
    public long? MaxPerPlayer { get; init; }

    /// <summary>New display order.</summary>
    public long? SortOrder { get; init; }

    /// <summary>Replaces the whole sale setting; an empty <see cref="PackAvailabilityRequest"/> removes every limit.</summary>
    public PackAvailabilityRequest? Availability { get; init; }

    /// <summary>
    /// <see cref="PackStatus.Draft"/>, <see cref="PackStatus.Active"/> or <see cref="PackStatus.Archived"/>. It is applied after the
    /// rest of the change: turning a pack active checks that every currency and item it uses is still active, and when that check
    /// fails the content change stays and the status does not move.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>Fields to erase (sent as <c>null</c>).</summary>
    public PackFields Clear { get; init; }
}
