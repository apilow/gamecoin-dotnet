using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Apilow.GameCoin.Internal;

/// <summary>A response that is JSON but not the shape the API promises. Reported as <c>INVALID_RESPONSE</c>; names a path, never a value.</summary>
internal sealed class ShapeException : Exception
{
    public ShapeException(string path, string expected)
        : base($"{path} must be {expected}")
    {
    }
}

/// <summary>A response type together with its source-generated deserialiser and its shape check.</summary>
internal sealed class ResponseSpec<T>
{
    public ResponseSpec(JsonTypeInfo<T> typeInfo, Action<JsonElement> check)
    {
        TypeInfo = typeInfo;
        Check = check;
    }

    public JsonTypeInfo<T> TypeInfo { get; }

    public Action<JsonElement> Check { get; }
}

/// <summary>
/// The responses of the API, each with the check that runs before it is deserialised: every field the API documents must be
/// there with the right type (extra fields are ignored). Mirrors the API reference, field for field.
/// </summary>
internal static class Responses
{
    public static readonly ResponseSpec<Catalog> Catalog = new(GameCoinJsonContext.Default.Catalog, v => Shape.Catalog(v, "catalog"));
    public static readonly ResponseSpec<Player> Player = new(GameCoinJsonContext.Default.Player, v => Shape.Player(v, "player"));
    public static readonly ResponseSpec<PlayerToken> PlayerToken = new(GameCoinJsonContext.Default.PlayerToken, Shape.PlayerToken);
    public static readonly ResponseSpec<Wallet> Wallet = new(GameCoinJsonContext.Default.Wallet, v => Shape.Wallet(v, "wallet"));
    public static readonly ResponseSpec<Movement> Movement = new(GameCoinJsonContext.Default.Movement, Shape.Movement);
    public static readonly ResponseSpec<LedgerPage> LedgerPage = new(GameCoinJsonContext.Default.LedgerPage, Shape.LedgerPage);
    public static readonly ResponseSpec<InventoryListResponse> InventoryListResponse = new(GameCoinJsonContext.Default.InventoryListResponse, Shape.InventoryList);
    public static readonly ResponseSpec<InventoryChangeResponse> InventoryChangeResponse = new(GameCoinJsonContext.Default.InventoryChangeResponse, Shape.InventoryChange);
    public static readonly ResponseSpec<Purchase> Purchase = new(GameCoinJsonContext.Default.Purchase, Shape.Purchase);
    public static readonly ResponseSpec<Checkout> Checkout = new(GameCoinJsonContext.Default.Checkout, Shape.Checkout);
    public static readonly ResponseSpec<Order> Order = new(GameCoinJsonContext.Default.Order, v => Shape.Order(v, "order"));
    public static readonly ResponseSpec<Redemption> Redemption = new(GameCoinJsonContext.Default.Redemption, Shape.Redemption);
}

internal static class Shape
{
    private static JsonElement Obj(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new ShapeException(path, "an object");
        }

        return value;
    }

    // A missing property is an element of kind Undefined, which fails every type check below, as it should.
    private static JsonElement Prop(JsonElement o, string key) => o.TryGetProperty(key, out var value) ? value : default;

    /// <summary>The property exists and is not <c>null</c>: an optional object that is then checked.</summary>
    private static bool Given(JsonElement o, string key) => o.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null;

    private static void Str(JsonElement o, string key, string path)
    {
        if (Prop(o, key).ValueKind != JsonValueKind.String)
        {
            throw new ShapeException($"{path}.{key}", "a string");
        }
    }

    private static void StrOrNull(JsonElement o, string key, string path)
    {
        if (Prop(o, key).ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            throw new ShapeException($"{path}.{key}", "a string or null");
        }
    }

    private static void Int(JsonElement o, string key, string path)
    {
        var value = Prop(o, key);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out _))
        {
            throw new ShapeException($"{path}.{key}", "an integer");
        }
    }

    private static void IntOrNull(JsonElement o, string key, string path)
    {
        if (Prop(o, key).ValueKind != JsonValueKind.Null)
        {
            Int(o, key, path);
        }
    }

    private static void Bool(JsonElement o, string key, string path)
    {
        if (Prop(o, key).ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new ShapeException($"{path}.{key}", "a boolean");
        }
    }

    private static void Date(JsonElement o, string key, string path)
    {
        var value = Prop(o, key);
        if (value.ValueKind != JsonValueKind.String || !value.TryGetDateTimeOffset(out _))
        {
            throw new ShapeException($"{path}.{key}", "an ISO 8601 date-time");
        }
    }

    private static void DateOrNull(JsonElement o, string key, string path)
    {
        if (Prop(o, key).ValueKind != JsonValueKind.Null)
        {
            Date(o, key, path);
        }
    }

    private static void List(JsonElement o, string key, string path, Action<JsonElement, string> item)
    {
        var value = Prop(o, key);
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new ShapeException($"{path}.{key}", "an array");
        }

        var index = 0;
        foreach (var element in value.EnumerateArray())
        {
            item(element, $"{path}.{key}[{index++}]");
        }
    }

    private static void StringMap(JsonElement o, string key, string path)
    {
        var map = Obj(Prop(o, key), $"{path}.{key}");
        foreach (var entry in map.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String)
            {
                throw new ShapeException($"{path}.{key}.{entry.Name}", "a string");
            }
        }
    }

    // --- Objects --------------------------------------------------------------------------------------------------

    public static void Player(JsonElement value, string path)
    {
        var o = Obj(value, path);
        Str(o, "id", path);
        StrOrNull(o, "externalId", path);
        Str(o, "kind", path);
        StrOrNull(o, "displayName", path);
        StrOrNull(o, "country", path);
        Bool(o, "blocked", path);
        Date(o, "createdAt", path);
    }

    public static void Balance(JsonElement value, string path)
    {
        var o = Obj(value, path);
        Str(o, "currency", path);
        Int(o, "paid", path);
        Int(o, "bonus", path);
        Int(o, "total", path);
    }

    public static void Wallet(JsonElement value, string path)
    {
        var o = Obj(value, path);
        Str(o, "playerId", path);
        List(o, "balances", path, Balance);
    }

    public static void LedgerEntry(JsonElement value, string path)
    {
        var o = Obj(value, path);
        var after = Obj(Prop(o, "balanceAfter"), $"{path}.balanceAfter");
        Int(after, "paid", $"{path}.balanceAfter");
        Int(after, "bonus", $"{path}.balanceAfter");
        Int(after, "total", $"{path}.balanceAfter");
        var reference = Obj(Prop(o, "ref"), $"{path}.ref");
        if (reference.TryGetProperty("orderId", out _))
        {
            Str(reference, "orderId", $"{path}.ref");
        }

        if (reference.TryGetProperty("itemSku", out _))
        {
            Str(reference, "itemSku", $"{path}.ref");
        }

        Str(o, "id", path);
        Str(o, "type", path);
        Str(o, "currency", path);
        Int(o, "paidDelta", path);
        Int(o, "bonusDelta", path);
        StrOrNull(o, "reason", path);
        StringMap(o, "metadata", path);
        Date(o, "createdAt", path);
    }

    public static void InventoryItem(JsonElement value, string path)
    {
        var o = Obj(value, path);
        Str(o, "sku", path);
        Int(o, "quantity", path);
        Date(o, "updatedAt", path);
    }

    private static void CatalogCurrency(JsonElement value, string path)
    {
        var o = Obj(value, path);
        Str(o, "code", path);
        Str(o, "name", path);
        Bool(o, "purchasable", path);
    }

    private static void CatalogItem(JsonElement value, string path)
    {
        var o = Obj(value, path);
        Str(o, "sku", path);
        Str(o, "name", path);
        StrOrNull(o, "description", path);
        Str(o, "type", path);
        if (Prop(o, "price").ValueKind != JsonValueKind.Null)
        {
            var price = Obj(Prop(o, "price"), $"{path}.price");
            Str(price, "currency", $"{path}.price");
            Int(price, "amount", $"{path}.price");
        }

        IntOrNull(o, "maxOwned", path);
        Bool(o, "clientPurchasable", path);
        StringMap(o, "metadata", path);
    }

    private static void CatalogPack(JsonElement value, string path)
    {
        var o = Obj(value, path);
        Str(o, "sku", path);
        Str(o, "name", path);
        StrOrNull(o, "description", path);
        Int(o, "priceCents", path);
        Str(o, "currency", path);
        List(o, "grants", path, (grant, grantPath) =>
        {
            var g = Obj(grant, grantPath);
            Str(g, "currency", grantPath);
            Int(g, "amount", grantPath);
            Int(g, "bonus", grantPath);
        });
        List(o, "items", path, (line, linePath) =>
        {
            var l = Obj(line, linePath);
            Str(l, "sku", linePath);
            Int(l, "quantity", linePath);
        });
        StrOrNull(o, "badge", path);
        IntOrNull(o, "maxPerPlayer", path);

        // 0.3: null for an ordinary pack. A key that is absent (an API older than sprint 3) reads as null; a wrong shape is refused.
        if (Given(o, "availability"))
        {
            var availability = Obj(Prop(o, "availability"), $"{path}.availability");
            DateOrNull(availability, "startsAt", $"{path}.availability");
            DateOrNull(availability, "endsAt", $"{path}.availability");
            IntOrNull(availability, "remaining", $"{path}.availability");
            Bool(availability, "founder", $"{path}.availability");
        }
    }

    public static void Catalog(JsonElement value, string path)
    {
        var o = Obj(value, path);
        List(o, "currencies", path, CatalogCurrency);
        List(o, "items", path, CatalogItem);
        List(o, "packs", path, CatalogPack);

        // 0.3: absent (an API older than sprint 3) reads as false / null; a wrong shape is refused.
        if (o.TryGetProperty("prelaunch", out _))
        {
            Bool(o, "prelaunch", path);
        }

        if (o.TryGetProperty("launchAt", out _))
        {
            DateOrNull(o, "launchAt", path);
        }
    }

    public static void Order(JsonElement value, string path)
    {
        var o = Obj(value, path);
        Str(o, "id", path);
        Str(o, "playerId", path);
        Str(o, "status", path);
        var pack = Obj(Prop(o, "pack"), $"{path}.pack");
        Str(pack, "sku", $"{path}.pack");
        Str(pack, "name", $"{path}.pack");
        Int(o, "amountCents", path);
        Str(o, "currency", path);
        Str(o, "country", path);
        Int(o, "vatRateBp", path);
        Int(o, "vatCents", path);
        Int(o, "netCents", path);
        Date(o, "createdAt", path);
        DateOrNull(o, "paidAt", path);
        DateOrNull(o, "fulfilledAt", path);
        DateOrNull(o, "refundedAt", path);

        // 0.3: null when the order has no creator code. Absent reads as null too, so that an event sent before sprint 3 is still
        // accepted; a wrong shape is refused.
        if (Given(o, "creatorCode"))
        {
            var creator = Obj(Prop(o, "creatorCode"), $"{path}.creatorCode");
            Str(creator, "code", $"{path}.creatorCode");
            Str(creator, "creatorName", $"{path}.creatorCode");
        }
    }

    public static void Redemption(JsonElement value)
    {
        var o = Obj(value, "response");
        List(o, "grants", "response", (grant, grantPath) =>
        {
            var g = Obj(grant, grantPath);
            Str(g, "currency", grantPath);
            Int(g, "amount", grantPath);
            Balance(Prop(g, "balance"), $"{grantPath}.balance");
        });
        List(o, "items", "response", RedemptionItem);
        List(o, "skipped", "response", RedemptionItem);
    }

    private static void RedemptionItem(JsonElement value, string path)
    {
        var o = Obj(value, path);
        Str(o, "sku", path);
        Int(o, "quantity", path);
    }

    // --- Webhook events -------------------------------------------------------------------------------------------

    /// <summary>The envelope every webhook body has: <c>id</c>, <c>type</c>, <c>createdAt</c>, <c>env</c> and a <c>data</c> of any shape.</summary>
    public static void WebhookEnvelope(JsonElement value)
    {
        var o = Obj(value, "event");
        Str(o, "id", "event");
        Str(o, "type", "event");
        Date(o, "createdAt", "event");
        Str(o, "env", "event");
        if (!o.TryGetProperty("data", out _))
        {
            throw new ShapeException("event.data", "present");
        }
    }

    public static void WalletCredited(JsonElement value, string path)
    {
        var o = Obj(value, path);
        Str(o, "playerId", path);
        Str(o, "orderId", path);
        List(o, "credits", path, (credit, creditPath) =>
        {
            var c = Obj(credit, creditPath);
            Str(c, "currency", creditPath);
            Int(c, "paid", creditPath);
            Int(c, "bonus", creditPath);
        });
    }

    public static void WebhookTest(JsonElement value, string path)
    {
        var o = Obj(value, path);
        Str(o, "message", path);
    }

    // --- Composed responses ---------------------------------------------------------------------------------------

    public static void PlayerToken(JsonElement value)
    {
        var o = Obj(value, "response");
        Str(o, "token", "response");
        Date(o, "expiresAt", "response");
    }

    public static void Movement(JsonElement value)
    {
        var o = Obj(value, "response");
        LedgerEntry(Prop(o, "entry"), "response.entry");
        Balance(Prop(o, "balance"), "response.balance");
    }

    public static void LedgerPage(JsonElement value)
    {
        var o = Obj(value, "response");
        List(o, "entries", "response", LedgerEntry);
        StrOrNull(o, "nextCursor", "response");
    }

    public static void InventoryList(JsonElement value)
    {
        var o = Obj(value, "response");
        List(o, "items", "response", InventoryItem);
    }

    public static void InventoryChange(JsonElement value)
    {
        var o = Obj(value, "response");
        InventoryItem(Prop(o, "item"), "response.item");
    }

    public static void Purchase(JsonElement value)
    {
        var o = Obj(value, "response");
        LedgerEntry(Prop(o, "entry"), "response.entry");
        Balance(Prop(o, "balance"), "response.balance");
        InventoryItem(Prop(o, "item"), "response.item");
    }

    public static void Checkout(JsonElement value)
    {
        var o = Obj(value, "response");
        Order(Prop(o, "order"), "response.order");
        Str(o, "checkoutUrl", "response");
    }
}
