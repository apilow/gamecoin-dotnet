using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Apilow.GameCoin;

// Immutable response objects, field for field as in the GameCoin API reference. Unknown fields of a response are ignored.
// String-typed enumerations (kind, type, status) stay strings so that a value added by the API later never breaks parsing;
// the constants of PlayerKind, ItemType, LedgerEntryType and OrderStatus name the known ones.

/// <summary>Known values of <see cref="Player.Kind"/>.</summary>
public static class PlayerKind
{
    /// <summary>A player created by your backend (<c>ext:</c> reference).</summary>
    public const string External = "external";

    /// <summary>An anonymous player created by the browser SDK.</summary>
    public const string Hosted = "hosted";
}

/// <summary>Known values of <see cref="CatalogItem.Type"/>.</summary>
public static class ItemType
{
    /// <summary>Used up by <see cref="InventoryResource.ConsumeAsync"/>.</summary>
    public const string Consumable = "consumable";

    /// <summary>Kept; usually capped by <see cref="CatalogItem.MaxOwned"/>.</summary>
    public const string Durable = "durable";
}

/// <summary>Known values of <see cref="LedgerEntry.Type"/>.</summary>
public static class LedgerEntryType
{
    /// <summary>Currency credited by a paid order.</summary>
    public const string PurchaseCredit = "purchase_credit";

    /// <summary>Currency granted by your backend (credits the bonus bucket).</summary>
    public const string Grant = "grant";

    /// <summary>Currency spent.</summary>
    public const string Spend = "spend";

    /// <summary>Currency taken back after a refund.</summary>
    public const string RefundClawback = "refund_clawback";

    /// <summary>Currency taken back after a chargeback.</summary>
    public const string ChargebackClawback = "chargeback_clawback";

    /// <summary>Manual correction.</summary>
    public const string Adjustment = "adjustment";
}

/// <summary>Known values of <see cref="Order.Status"/>.</summary>
public static class OrderStatus
{
    /// <summary>Created, waiting for the payment.</summary>
    public const string Pending = "pending";

    /// <summary>Paid, not delivered yet.</summary>
    public const string Paid = "paid";

    /// <summary>Paid and delivered (currencies credited, items granted).</summary>
    public const string Fulfilled = "fulfilled";

    /// <summary>Refunded; the coins and items were taken back.</summary>
    public const string Refunded = "refunded";

    /// <summary>Disputed by the cardholder.</summary>
    public const string Chargeback = "chargeback";

    /// <summary>The payment failed.</summary>
    public const string Failed = "failed";

    /// <summary>Canceled by the player.</summary>
    public const string Canceled = "canceled";

    /// <summary>Not paid in time.</summary>
    public const string Expired = "expired";
}

/// <summary>A player.</summary>
public sealed record Player
{
    /// <summary>GameCoin player id.</summary>
    public string Id { get; init; } = "";

    /// <summary>Your own id for the player (<c>ext:</c> players), otherwise <c>null</c>.</summary>
    public string? ExternalId { get; init; }

    /// <summary><c>external</c> or <c>hosted</c>, see <see cref="PlayerKind"/>.</summary>
    public string Kind { get; init; } = "";

    /// <summary>Display name, if any.</summary>
    public string? DisplayName { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country, if known.</summary>
    public string? Country { get; init; }

    /// <summary><c>true</c> when the player is blocked.</summary>
    public bool Blocked { get; init; }

    /// <summary>Creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>The balance of one currency.</summary>
public sealed record Balance
{
    /// <summary>Currency code (<c>gems</c>).</summary>
    public string Currency { get; init; } = "";

    /// <summary>Amount bought with real money.</summary>
    public long Paid { get; init; }

    /// <summary>Amount granted for free (bonuses, rewards).</summary>
    public long Bonus { get; init; }

    /// <summary><see cref="Paid"/> plus <see cref="Bonus"/>.</summary>
    public long Total { get; init; }
}

/// <summary>The balances of a player: one line per active currency, zeros included.</summary>
public sealed record Wallet
{
    /// <summary>GameCoin player id.</summary>
    public string PlayerId { get; init; } = "";

    /// <summary>One balance per active currency.</summary>
    public IReadOnlyList<Balance> Balances { get; init; } = Array.Empty<Balance>();
}

/// <summary>The balance after a ledger entry.</summary>
public sealed record BalanceSnapshot
{
    /// <summary>Paid bucket after the entry.</summary>
    public long Paid { get; init; }

    /// <summary>Bonus bucket after the entry.</summary>
    public long Bonus { get; init; }

    /// <summary>Total after the entry.</summary>
    public long Total { get; init; }
}

/// <summary>What a ledger entry refers to.</summary>
public sealed record LedgerRef
{
    /// <summary>The order, for entries caused by an order.</summary>
    public string? OrderId { get; init; }

    /// <summary>The item sku, for entries caused by a purchase of an item.</summary>
    public string? ItemSku { get; init; }
}

/// <summary>An immutable line of the ledger.</summary>
public sealed record LedgerEntry
{
    /// <summary>Entry id.</summary>
    public string Id { get; init; } = "";

    /// <summary>Kind of movement, see <see cref="LedgerEntryType"/>.</summary>
    public string Type { get; init; } = "";

    /// <summary>Currency code.</summary>
    public string Currency { get; init; } = "";

    /// <summary>Change of the paid bucket (negative when spent).</summary>
    public long PaidDelta { get; init; }

    /// <summary>Change of the bonus bucket (negative when spent).</summary>
    public long BonusDelta { get; init; }

    /// <summary>Balance right after this entry.</summary>
    public BalanceSnapshot BalanceAfter { get; init; } = new();

    /// <summary>Free-form reason you gave, if any.</summary>
    public string? Reason { get; init; }

    /// <summary>Free-form metadata you gave (empty when none).</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = Empty.Metadata;

    /// <summary>Order or item the entry refers to.</summary>
    public LedgerRef Ref { get; init; } = new();

    /// <summary>Creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>A page of the ledger, newest entries first.</summary>
public sealed record LedgerPage
{
    /// <summary>The entries of the page.</summary>
    public IReadOnlyList<LedgerEntry> Entries { get; init; } = Array.Empty<LedgerEntry>();

    /// <summary>Cursor of the next page, <c>null</c> on the last page.</summary>
    public string? NextCursor { get; init; }
}

/// <summary>A quantity of an item owned by a player.</summary>
public sealed record InventoryItem
{
    /// <summary>Item sku.</summary>
    public string Sku { get; init; } = "";

    /// <summary>Quantity owned (0 is possible right after a consume).</summary>
    public long Quantity { get; init; }

    /// <summary>Last change.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// <c>true</c> when the response to <see cref="InventoryResource.GrantAsync"/> or <see cref="InventoryResource.ConsumeAsync"/>
    /// was the replay of an earlier request with the same idempotency key. Always <c>false</c> elsewhere.
    /// </summary>
    [JsonIgnore]
    public bool Replayed { get; init; }
}

/// <summary>The result of a wallet grant or spend.</summary>
public sealed record Movement
{
    /// <summary>The ledger entry written.</summary>
    public LedgerEntry Entry { get; init; } = new();

    /// <summary>The balance of the currency after the movement.</summary>
    public Balance Balance { get; init; } = new();

    /// <summary><c>true</c> when the response was the replay of an earlier request with the same idempotency key.</summary>
    [JsonIgnore]
    public bool Replayed { get; init; }
}

/// <summary>The result of the purchase of an item with its currency price.</summary>
public sealed record Purchase
{
    /// <summary>The ledger entry written.</summary>
    public LedgerEntry Entry { get; init; } = new();

    /// <summary>The balance of the currency after the purchase.</summary>
    public Balance Balance { get; init; } = new();

    /// <summary>The item with its new quantity.</summary>
    public InventoryItem Item { get; init; } = new();

    /// <summary><c>true</c> when the response was the replay of an earlier request with the same idempotency key.</summary>
    [JsonIgnore]
    public bool Replayed { get; init; }
}

/// <summary>A currency of the catalog.</summary>
public sealed record CatalogCurrency
{
    /// <summary>Currency code.</summary>
    public string Code { get; init; } = "";

    /// <summary>Display name.</summary>
    public string Name { get; init; } = "";

    /// <summary><c>true</c> when it can be bought with real money (it then has a paid bucket).</summary>
    public bool Purchasable { get; init; }
}

/// <summary>The price of an item in a currency.</summary>
public sealed record ItemPrice
{
    /// <summary>Currency code.</summary>
    public string Currency { get; init; } = "";

    /// <summary>Amount.</summary>
    public long Amount { get; init; }
}

/// <summary>An item of the catalog.</summary>
public sealed record CatalogItem
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
}

/// <summary>A currency amount credited by a pack.</summary>
public sealed record PackGrant
{
    /// <summary>Currency code.</summary>
    public string Currency { get; init; } = "";

    /// <summary>Amount credited to the paid bucket.</summary>
    public long Amount { get; init; }

    /// <summary>Extra amount credited to the bonus bucket.</summary>
    public long Bonus { get; init; }
}

/// <summary>An item delivered by a pack.</summary>
public sealed record PackItem
{
    /// <summary>Item sku.</summary>
    public string Sku { get; init; } = "";

    /// <summary>Quantity delivered.</summary>
    public long Quantity { get; init; }
}

/// <summary>A pack of the catalog: what a player buys with real money.</summary>
public sealed record CatalogPack
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

    /// <summary>Badge to display ("best value"...), if any.</summary>
    public string? Badge { get; init; }

    /// <summary>Maximum number of purchases per player, <c>null</c> for no limit.</summary>
    public long? MaxPerPlayer { get; init; }
}

/// <summary>The active currencies, items and packs of the game.</summary>
public sealed record Catalog
{
    /// <summary>Currencies.</summary>
    public IReadOnlyList<CatalogCurrency> Currencies { get; init; } = Array.Empty<CatalogCurrency>();

    /// <summary>Items.</summary>
    public IReadOnlyList<CatalogItem> Items { get; init; } = Array.Empty<CatalogItem>();

    /// <summary>Packs.</summary>
    public IReadOnlyList<CatalogPack> Packs { get; init; } = Array.Empty<CatalogPack>();
}

/// <summary>The pack an order was made for, as it was at the time of the purchase.</summary>
public sealed record OrderPack
{
    /// <summary>Pack sku.</summary>
    public string Sku { get; init; } = "";

    /// <summary>Pack name.</summary>
    public string Name { get; init; } = "";
}

/// <summary>An order: the purchase of a pack with real money.</summary>
public sealed record Order
{
    /// <summary>Order id.</summary>
    public string Id { get; init; } = "";

    /// <summary>GameCoin player id.</summary>
    public string PlayerId { get; init; } = "";

    /// <summary>State of the order, see <see cref="OrderStatus"/>.</summary>
    public string Status { get; init; } = "";

    /// <summary>The pack.</summary>
    public OrderPack Pack { get; init; } = new();

    /// <summary>Amount charged in cents, VAT included.</summary>
    public long AmountCents { get; init; }

    /// <summary>Always <c>EUR</c>.</summary>
    public string Currency { get; init; } = "";

    /// <summary>Country used for the VAT (ISO 3166-1 alpha-2).</summary>
    public string Country { get; init; } = "";

    /// <summary>VAT rate in basis points (2100 = 21 %).</summary>
    public long VatRateBp { get; init; }

    /// <summary>VAT in cents.</summary>
    public long VatCents { get; init; }

    /// <summary>Amount without VAT in cents.</summary>
    public long NetCents { get; init; }

    /// <summary>Creation time.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Payment time, <c>null</c> until paid.</summary>
    public DateTimeOffset? PaidAt { get; init; }

    /// <summary>Delivery time, <c>null</c> until fulfilled.</summary>
    public DateTimeOffset? FulfilledAt { get; init; }

    /// <summary>Refund time, <c>null</c> unless refunded.</summary>
    public DateTimeOffset? RefundedAt { get; init; }
}

/// <summary>The result of the creation of a checkout.</summary>
public sealed record Checkout
{
    /// <summary>The order (<c>pending</c> until paid).</summary>
    public Order Order { get; init; } = new();

    /// <summary>The hosted payment page to send the player to.</summary>
    public string CheckoutUrl { get; init; } = "";

    /// <summary>
    /// <c>true</c> when the response was the replay of an earlier request with the same idempotency key (the order is then
    /// returned in its current state).
    /// </summary>
    [JsonIgnore]
    public bool Replayed { get; init; }
}

/// <summary>A short-lived player token, for the browser SDK.</summary>
public sealed record PlayerToken
{
    /// <summary>The token (<c>gc_pt_…</c>). Give it to the player's browser; do not log it.</summary>
    public string Token { get; init; } = "";

    /// <summary>Expiry time.</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{nameof(PlayerToken)} {{ Token = ***, ExpiresAt = {ExpiresAt:O} }}";
}

internal static class Empty
{
    internal static readonly IReadOnlyDictionary<string, string> Metadata = new Dictionary<string, string>(0);
}
