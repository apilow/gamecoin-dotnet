namespace Apilow.GameCoin.Tests;

/// <summary>Every object of the API, field for field.</summary>
public class ParsingTests
{
    private static readonly PlayerRef User = PlayerRef.Ext("user-42");

    [Fact]
    public async Task Player_ignores_unknown_fields_and_keeps_nulls()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Player);

        var player = await rig.Client.Players.GetAsync(User);

        Assert.Equal("6ac40ac02e9fe50981a1e085", player.Id);
        Assert.Equal("probe-190", player.ExternalId);
        Assert.Equal(PlayerKind.External, player.Kind);
        Assert.Equal("Probe", player.DisplayName);
        Assert.Null(player.Country);
        Assert.False(player.Blocked);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 20, 38, 24, 539, TimeSpan.Zero), player.CreatedAt);
    }

    [Fact]
    public async Task Hosted_player_has_no_external_id()
    {
        var rig = new TestRig();
        rig.Handler.Ok("""{"id":"p9","externalId":null,"kind":"hosted","displayName":null,"country":"FR","blocked":true,"createdAt":"2026-01-02T03:04:05.000Z"}""");

        var player = await rig.Client.Players.GetAsync("p9");

        Assert.Null(player.ExternalId);
        Assert.Equal(PlayerKind.Hosted, player.Kind);
        Assert.Null(player.DisplayName);
        Assert.Equal("FR", player.Country);
        Assert.True(player.Blocked);
    }

    [Fact]
    public async Task Player_token()
    {
        var rig = new TestRig();
        rig.Handler.Ok("""{"token":"gc_pt_abc","expiresAt":"2026-10-05T21:38:32.000Z"}""");

        var token = await rig.Client.Players.CreateTokenAsync(User);

        Assert.Equal("gc_pt_abc", token.Token);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 21, 38, 32, TimeSpan.Zero), token.ExpiresAt);
        Assert.DoesNotContain("gc_pt_abc", token.ToString());
    }

    [Fact]
    public async Task Wallet_with_one_line_per_currency()
    {
        var rig = new TestRig();
        rig.Handler.Ok("""{"playerId":"p1","balances":[{"currency":"gems","paid":40,"bonus":60,"total":100},{"currency":"gold","paid":0,"bonus":0,"total":0}]}""");

        var wallet = await rig.Client.Wallet.GetAsync(User);

        Assert.Equal("p1", wallet.PlayerId);
        Assert.Equal(2, wallet.Balances.Count);
        Assert.Equal(new Balance { Currency = "gems", Paid = 40, Bonus = 60, Total = 100 }, wallet.Balances[0]);
        Assert.Equal(0, wallet.Balances[1].Total);
    }

    [Fact]
    public async Task Movement_entry_and_balance()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Movement);

        var movement = await rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 100 });

        Assert.Equal(100, movement.Balance.Total);
        Assert.Equal(100, movement.Balance.Bonus);
        Assert.Equal(0, movement.Balance.Paid);
        Assert.Equal("gems", movement.Balance.Currency);
        var entry = movement.Entry;
        Assert.Equal("6ac40ac312fe8b4190d46ffc", entry.Id);
        Assert.Equal(LedgerEntryType.Grant, entry.Type);
        Assert.Equal("gems", entry.Currency);
        Assert.Equal(0, entry.PaidDelta);
        Assert.Equal(100, entry.BonusDelta);
        Assert.Equal(100, entry.BalanceAfter.Total);
        Assert.Equal("level_up", entry.Reason);
        Assert.Equal("b", entry.Metadata["a"]);
        Assert.Null(entry.Ref.OrderId);
        Assert.Null(entry.Ref.ItemSku);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 20, 38, 27, 227, TimeSpan.Zero), entry.CreatedAt);
        Assert.False(movement.Replayed);
    }

    [Fact]
    public async Task Ledger_page_with_refs_and_a_cursor()
    {
        var rig = new TestRig();
        rig.Handler.Ok("""
            {"entries":[
              {"id":"e2","type":"spend","currency":"gems","paidDelta":-20,"bonusDelta":-10,"balanceAfter":{"paid":0,"bonus":70,"total":70},"reason":null,"metadata":{},"ref":{"itemSku":"potion"},"createdAt":"2026-10-05T20:39:00.000Z"},
              {"id":"e1","type":"purchase_credit","currency":"gems","paidDelta":500,"bonusDelta":50,"balanceAfter":{"paid":500,"bonus":50,"total":550},"reason":null,"metadata":{},"ref":{"orderId":"o1"},"createdAt":"2026-10-05T20:38:00.000Z"}
            ],"nextCursor":"abc"}
            """);

        var page = await rig.Client.Ledger.ListAsync(User);

        Assert.Equal("abc", page.NextCursor);
        Assert.Equal(2, page.Entries.Count);
        Assert.Equal("potion", page.Entries[0].Ref.ItemSku);
        Assert.Null(page.Entries[0].Reason);
        Assert.Empty(page.Entries[0].Metadata);
        Assert.Equal(-20, page.Entries[0].PaidDelta);
        Assert.Equal(-10, page.Entries[0].BonusDelta);
        Assert.Equal("o1", page.Entries[1].Ref.OrderId);
        Assert.Equal(LedgerEntryType.PurchaseCredit, page.Entries[1].Type);
    }

    [Fact]
    public async Task Last_ledger_page_has_a_null_cursor()
    {
        var rig = new TestRig();
        rig.Handler.Ok("""{"entries":[],"nextCursor":null}""");

        var page = await rig.Client.Ledger.ListAsync(User);

        Assert.Null(page.NextCursor);
        Assert.Empty(page.Entries);
    }

    [Fact]
    public async Task Inventory_list_and_changes()
    {
        var rig = new TestRig();
        rig.Handler.Ok($$"""{"items":[{{Samples.Item}},{"sku":"fire-sword","quantity":1,"updatedAt":"2026-10-05T20:41:00.000Z"}]}""")
            .Ok($$"""{"item":{{Samples.Item}}}""")
            .Ok("""{"item":{"sku":"potion","quantity":0,"updatedAt":"2026-10-05T20:42:00.000Z"}}""");

        var items = await rig.Client.Inventory.ListAsync(User);
        var granted = await rig.Client.Inventory.GrantAsync(User, new InventoryGrantRequest { Sku = "potion" });
        var consumed = await rig.Client.Inventory.ConsumeAsync(User, new InventoryConsumeRequest { Sku = "potion", Quantity = 3 });

        Assert.Equal(2, items.Count);
        Assert.Equal("potion", items[0].Sku);
        Assert.Equal(3, items[0].Quantity);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 20, 40, 0, TimeSpan.Zero), items[0].UpdatedAt);
        Assert.Equal("fire-sword", items[1].Sku);
        Assert.Equal(3, granted.Quantity);
        Assert.Equal(0, consumed.Quantity); // returned even at 0
    }

    [Fact]
    public async Task Purchase_has_entry_balance_and_item()
    {
        var rig = new TestRig();
        rig.Handler.Ok($$"""{"entry":{{Samples.Entry}},"balance":{{Samples.Balance}},"item":{{Samples.Item}}}""");

        var purchase = await rig.Client.Purchases.CreateAsync(User, new PurchaseRequest { Sku = "potion" });

        Assert.Equal("grant", purchase.Entry.Type);
        Assert.Equal(100, purchase.Balance.Total);
        Assert.Equal("potion", purchase.Item.Sku);
        Assert.Equal(3, purchase.Item.Quantity);
        Assert.False(purchase.Replayed);
    }

    [Fact]
    public async Task Pending_order_and_checkout_url()
    {
        var rig = new TestRig();
        rig.Handler.Reply(
            System.Net.HttpStatusCode.Created,
            $$"""{"order":{{Samples.Order}},"checkoutUrl":"https://gc.example.test/pay/o1?t=abc"}""");

        var checkout = await rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "gems-500" });

        Assert.Equal("https://gc.example.test/pay/o1?t=abc", checkout.CheckoutUrl);
        var order = checkout.Order;
        Assert.Equal("o1", order.Id);
        Assert.Equal("p1", order.PlayerId);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal("gems-500", order.Pack.Sku);
        Assert.Equal("Bag of gems", order.Pack.Name);
        Assert.Equal(499, order.AmountCents);
        Assert.Equal("EUR", order.Currency);
        Assert.Equal("BE", order.Country);
        Assert.Equal(2100, order.VatRateBp);
        Assert.Equal(87, order.VatCents);
        Assert.Equal(412, order.NetCents);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 20, 38, 24, 539, TimeSpan.Zero), order.CreatedAt);
        Assert.Null(order.PaidAt);
        Assert.Null(order.FulfilledAt);
        Assert.Null(order.RefundedAt);
    }

    [Fact]
    public async Task Fulfilled_and_refunded_order_dates()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Order.Replace("\"status\":\"pending\"", "\"status\":\"refunded\"")
            .Replace("\"paidAt\":null", "\"paidAt\":\"2026-10-05T20:40:00.000Z\"")
            .Replace("\"fulfilledAt\":null", "\"fulfilledAt\":\"2026-10-05T20:40:01.000Z\"")
            .Replace("\"refundedAt\":null", "\"refundedAt\":\"2026-10-05T20:50:00.000+02:00\""));

        var order = await rig.Client.Orders.GetAsync("o1");

        Assert.Equal(OrderStatus.Refunded, order.Status);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 20, 40, 0, TimeSpan.Zero), order.PaidAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 20, 40, 1, TimeSpan.Zero), order.FulfilledAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 18, 50, 0, TimeSpan.Zero), order.RefundedAt); // offsets are honoured
    }

    [Fact]
    public async Task Catalog_currencies_items_and_packs()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Catalog);

        var catalog = await rig.Client.Catalog.GetAsync();

        Assert.Equal(2, catalog.Currencies.Count);
        Assert.Equal(new CatalogCurrency { Code = "gems", Name = "Gems", Purchasable = true }, catalog.Currencies[0]);
        Assert.False(catalog.Currencies[1].Purchasable);

        Assert.Equal(2, catalog.Items.Count);
        var potion = catalog.Items[0];
        Assert.Equal("potion", potion.Sku);
        Assert.Null(potion.Description);
        Assert.Equal(ItemType.Consumable, potion.Type);
        Assert.Equal(new ItemPrice { Currency = "gems", Amount = 20 }, potion.Price);
        Assert.Null(potion.MaxOwned);
        Assert.True(potion.ClientPurchasable);
        Assert.Empty(potion.Metadata);
        var sword = catalog.Items[1];
        Assert.Equal("Hot", sword.Description);
        Assert.Equal(ItemType.Durable, sword.Type);
        Assert.Equal(1, sword.MaxOwned);
        Assert.Equal("3", sword.Metadata["tier"]);

        var pack = Assert.Single(catalog.Packs);
        Assert.Equal("starter", pack.Sku);
        Assert.Equal(99, pack.PriceCents);
        Assert.Equal("EUR", pack.Currency);
        Assert.Equal(new PackGrant { Currency = "gems", Amount = 100, Bonus = 20 }, Assert.Single(pack.Grants));
        Assert.Equal(new PackItem { Sku = "potion", Quantity = 1 }, Assert.Single(pack.Items));
        Assert.Equal("new", pack.Badge);
        Assert.Equal(1, pack.MaxPerPlayer);
    }

    [Fact]
    public async Task Item_without_a_price_and_pack_without_a_limit()
    {
        var rig = new TestRig();
        rig.Handler.Ok("""{"currencies":[],"items":[{"sku":"gift","name":"Gift","description":null,"type":"durable","price":null,"maxOwned":null,"clientPurchasable":false,"metadata":{}}],"packs":[{"sku":"p","name":"P","description":null,"priceCents":1,"currency":"EUR","grants":[],"items":[],"badge":null,"maxPerPlayer":null}]}""");

        var catalog = await rig.Client.Catalog.GetAsync();

        Assert.Null(catalog.Items[0].Price);
        Assert.False(catalog.Items[0].ClientPurchasable);
        Assert.Null(catalog.Packs[0].MaxPerPlayer);
        Assert.Null(catalog.Packs[0].Badge);
        Assert.Empty(catalog.Packs[0].Grants);
    }

    [Fact]
    public async Task Unknown_enumeration_values_do_not_break_parsing()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Order.Replace("\"status\":\"pending\"", "\"status\":\"disputed_v2\""));

        var order = await rig.Client.Orders.GetAsync("o1");

        Assert.Equal("disputed_v2", order.Status);
    }

    [Fact]
    public async Task Property_names_are_matched_exactly()
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Ok("""{"PlayerID":"p1","BALANCES":[]}""");

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Wallet.GetAsync(User));

        Assert.Equal(GameCoinErrorCodes.InvalidResponse, error.Code);
    }
}
