using System;
using System.Collections.Generic;

namespace Apilow.GameCoin;

/// <summary>Names of the webhook events, as found in <see cref="WebhookEvent.Type"/>.</summary>
public static class WebhookEventTypes
{
    /// <summary>An order is paid and delivered: the currency and the items are credited. <see cref="WebhookEvent.Data"/> is an <see cref="Order"/>.</summary>
    public const string OrderFulfilled = "order.fulfilled";

    /// <summary>An order is refunded; its coins and items are taken back. <see cref="WebhookEvent.Data"/> is an <see cref="Order"/>.</summary>
    public const string OrderRefunded = "order.refunded";

    /// <summary>The payer disputes the payment; its coins and items are taken back. <see cref="WebhookEvent.Data"/> is an <see cref="Order"/>.</summary>
    public const string OrderChargeback = "order.chargeback";

    /// <summary>A delivery added currency to a wallet. <see cref="WebhookEvent.Data"/> is a <see cref="WalletCreditedData"/>.</summary>
    public const string WalletCredited = "wallet.credited";

    /// <summary>A test event sent from the dashboard. <see cref="WebhookEvent.Data"/> is a <see cref="WebhookTestData"/>.</summary>
    public const string WebhookTest = "webhook.test";
}

/// <summary>
/// A verified webhook event, the envelope GameCoin sends. <see cref="Data"/> is typed by <see cref="Type"/>: an <see cref="Order"/>
/// for the <c>order.*</c> events, a <see cref="WalletCreditedData"/> for <c>wallet.credited</c>, a <see cref="WebhookTestData"/>
/// for <c>webhook.test</c>, and the untouched <see cref="System.Text.Json.JsonElement"/> for a type this version does not know.
/// </summary>
/// <example>
/// <code>
/// switch (webhookEvent.Data)
/// {
///     case Order order when webhookEvent.Type == WebhookEventTypes.OrderFulfilled:
///         Unlock(order.PlayerId, order.Pack.Sku);
///         break;
///     case WalletCreditedData credited:
///         RefreshWallet(credited.PlayerId);
///         break;
/// }
/// </code>
/// </example>
public sealed record WebhookEvent
{
    /// <summary>
    /// Id of the event. The same event always has the same id, so use it to process an event <b>once</b>: GameCoin can send it again.
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>Type of the event, see <see cref="WebhookEventTypes"/>. A type added later is passed through as a string.</summary>
    public string Type { get; init; } = "";

    /// <summary>When the event was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary><c>test</c> or <c>live</c>: the environment the event comes from.</summary>
    public string Env { get; init; } = "";

    /// <summary>The payload, typed by <see cref="Type"/> (see the remarks of <see cref="WebhookEvent"/>).</summary>
    public object Data { get; init; } = new();
}

/// <summary>What a delivered order added to one currency of a wallet.</summary>
public sealed record WalletCredit
{
    /// <summary>Currency code.</summary>
    public string Currency { get; init; } = "";

    /// <summary>Amount credited to the paid bucket.</summary>
    public long Paid { get; init; }

    /// <summary>Amount credited to the bonus bucket.</summary>
    public long Bonus { get; init; }
}

/// <summary><see cref="WebhookEvent.Data"/> of <c>wallet.credited</c>: which player received what.</summary>
public sealed record WalletCreditedData
{
    /// <summary>GameCoin player id.</summary>
    public string PlayerId { get; init; } = "";

    /// <summary>The order that was delivered.</summary>
    public string OrderId { get; init; } = "";

    /// <summary>The credits, one per currency.</summary>
    public IReadOnlyList<WalletCredit> Credits { get; init; } = Array.Empty<WalletCredit>();
}

/// <summary><see cref="WebhookEvent.Data"/> of <c>webhook.test</c>.</summary>
public sealed record WebhookTestData
{
    /// <summary>A short sentence.</summary>
    public string Message { get; init; } = "";
}
