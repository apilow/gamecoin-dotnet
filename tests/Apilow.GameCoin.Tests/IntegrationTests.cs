using System.Net.Http;

namespace Apilow.GameCoin.Tests;

/// <summary>A fact that is skipped unless GAMECOIN_SECRET_KEY and GAMECOIN_BASE_URL are set.</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (string.IsNullOrEmpty(IntegrationTests.SecretKey) || string.IsNullOrEmpty(IntegrationTests.BaseUrl))
        {
            Skip = "Set GAMECOIN_SECRET_KEY and GAMECOIN_BASE_URL to run the integration tests against the local sandbox.";
        }
    }
}

/// <summary>
/// Runs against the sandbox game created by <c>npm run sdk:sandbox</c> in the GameCoin application: currencies gems and gold;
/// items potion (consumable, 20 gems) and fire-sword (durable, 300 gems, one at most); packs starter and gems-500.
/// </summary>
public class IntegrationTests
{
    internal static string? SecretKey => Environment.GetEnvironmentVariable("GAMECOIN_SECRET_KEY");

    internal static string? BaseUrl => Environment.GetEnvironmentVariable("GAMECOIN_BASE_URL");

    /// <summary>Counts the GET requests that reach the ledger, to prove that iteration really pages.</summary>
    private sealed class CountingHandler : DelegatingHandler
    {
        public CountingHandler()
            : base(new HttpClientHandler())
        {
        }

        public int LedgerRequests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/ledger", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref LedgerRequests);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    [IntegrationFact]
    public async Task Full_flow_against_the_sandbox()
    {
        var counting = new CountingHandler();
        using var gamecoin = new GameCoinClient(SecretKey!, new GameCoinOptions { BaseUrl = BaseUrl!, HttpMessageHandler = counting });
        var player = PlayerRef.Ext("sdk-dotnet-" + Guid.NewGuid().ToString("N"));
        var ct = CancellationToken.None;
        Assert.Equal("test", gamecoin.Environment);

        // catalog
        var catalog = await gamecoin.Catalog.GetAsync(ct);
        Assert.Contains(catalog.Currencies, c => c.Code == "gems" && c.Purchasable);
        Assert.Contains(catalog.Currencies, c => c.Code == "gold" && !c.Purchasable);
        var potion = Assert.Single(catalog.Items, i => i.Sku == "potion");
        Assert.Equal(ItemType.Consumable, potion.Type);
        Assert.Equal(new ItemPrice { Currency = "gems", Amount = 20 }, potion.Price);
        var sword = Assert.Single(catalog.Items, i => i.Sku == "fire-sword");
        Assert.Equal(ItemType.Durable, sword.Type);
        Assert.Equal(300, sword.Price!.Amount);
        Assert.Equal(1, sword.MaxOwned);
        Assert.Contains(catalog.Packs, p => p.Sku == "starter");
        var gemsPack = Assert.Single(catalog.Packs, p => p.Sku == "gems-500");
        Assert.Equal("EUR", gemsPack.Currency);

        // upsert a fresh ext: player
        var created = await gamecoin.Players.UpsertAsync(player, new PlayerProfile { DisplayName = "SDK .NET test" }, ct);
        Assert.Equal(player.Value.Substring("ext:".Length), created.ExternalId);
        Assert.Equal(PlayerKind.External, created.Kind);
        Assert.Equal("SDK .NET test", created.DisplayName);
        Assert.False(created.Blocked);
        var fetched = await gamecoin.Players.GetAsync(player, ct);
        Assert.Equal(created.Id, fetched.Id);
        var byId = await gamecoin.Players.GetAsync(PlayerRef.FromId(created.Id), ct);
        Assert.Equal(created.Id, byId.Id);

        // grant 100 gems, then replay the same key
        var key = "sdk-dotnet-grant-" + Guid.NewGuid().ToString("N");
        var options = new RequestOptions { IdempotencyKey = key };
        var grant = new GrantRequest { Currency = "gems", Amount = 100, Reason = "level_up", Metadata = new Dictionary<string, string> { ["level"] = "7" } };
        var granted = await gamecoin.Wallet.GrantAsync(player, grant, options, ct);
        Assert.False(granted.Replayed);
        Assert.Equal(100, granted.Balance.Total);
        Assert.Equal(100, granted.Balance.Bonus);
        Assert.Equal(0, granted.Balance.Paid);
        Assert.Equal(LedgerEntryType.Grant, granted.Entry.Type);
        Assert.Equal("level_up", granted.Entry.Reason);
        Assert.Equal("7", granted.Entry.Metadata["level"]);
        Assert.Equal(100, granted.Entry.BonusDelta);

        var replayed = await gamecoin.Wallet.GrantAsync(player, grant, options, ct);
        Assert.True(replayed.Replayed);
        Assert.Equal(granted.Entry.Id, replayed.Entry.Id);
        Assert.Equal(100, replayed.Balance.Total); // not credited twice

        // the same key with another request is a conflict
        var conflict = await Assert.ThrowsAsync<GameCoinException>(() =>
            gamecoin.Wallet.GrantAsync(player, grant with { Amount = 101 }, options, ct));
        Assert.Equal(GameCoinErrorCodes.IdempotencyConflict, conflict.Code);
        Assert.Equal(409, conflict.Status);

        // spend 30
        var spent = await gamecoin.Wallet.SpendAsync(player, new SpendRequest { Currency = "gems", Amount = 30, Reason = "shop" }, cancellationToken: ct);
        Assert.False(spent.Replayed);
        Assert.Equal(70, spent.Balance.Total);
        Assert.Equal(LedgerEntryType.Spend, spent.Entry.Type);
        Assert.Equal(-30, spent.Entry.BonusDelta + spent.Entry.PaidDelta);

        // spend too much
        var broke = await Assert.ThrowsAsync<GameCoinException>(() =>
            gamecoin.Wallet.SpendAsync(player, new SpendRequest { Currency = "gems", Amount = 1000 }, cancellationToken: ct));
        Assert.Equal(GameCoinErrorCodes.InsufficientFunds, broke.Code);
        Assert.Equal(409, broke.Status);
        Assert.Equal(1000, broke.Details["required"].GetInt64());
        Assert.Equal(70, broke.Details["available"].GetInt64());

        // wallet
        var wallet = await gamecoin.Wallet.GetAsync(player, ct);
        Assert.Equal(created.Id, wallet.PlayerId);
        Assert.Equal(70, Assert.Single(wallet.Balances, b => b.Currency == "gems").Total);
        Assert.Equal(0, Assert.Single(wallet.Balances, b => b.Currency == "gold").Total);

        // ledger iteration, one entry per page
        var ledgerBefore = counting.LedgerRequests;
        var entries = new List<LedgerEntry>();
        await foreach (var entry in gamecoin.Ledger.IterateAsync(player, new LedgerQuery { Limit = 1 }, ct))
        {
            entries.Add(entry);
        }

        Assert.Equal(2, entries.Count);
        Assert.Equal(new[] { LedgerEntryType.Spend, LedgerEntryType.Grant }, entries.Select(e => e.Type)); // newest first
        Assert.Equal(new[] { spent.Entry.Id, granted.Entry.Id }, entries.Select(e => e.Id));
        Assert.True(counting.LedgerRequests - ledgerBefore >= 2, "iteration should have fetched several pages");
        var page = await gamecoin.Ledger.ListAsync(player, new LedgerQuery { Currency = "gems", Limit = 1 }, ct);
        Assert.Single(page.Entries);
        Assert.False(string.IsNullOrEmpty(page.NextCursor));
        var goldPage = await gamecoin.Ledger.ListAsync(player, new LedgerQuery { Currency = "gold" }, ct);
        Assert.Empty(goldPage.Entries);
        Assert.Null(goldPage.NextCursor);

        // player token
        var token = await gamecoin.Players.CreateTokenAsync(player, ct);
        Assert.StartsWith("gc_pt_", token.Token);
        Assert.True(token.ExpiresAt > DateTimeOffset.UtcNow);

        // grant and consume a potion
        var potionGrant = await gamecoin.Inventory.GrantAsync(player, new InventoryGrantRequest { Sku = "potion", Quantity = 2 }, cancellationToken: ct);
        Assert.Equal("potion", potionGrant.Sku);
        Assert.Equal(2, potionGrant.Quantity);
        Assert.False(potionGrant.Replayed);
        var potionConsume = await gamecoin.Inventory.ConsumeAsync(player, new InventoryConsumeRequest { Sku = "potion" }, cancellationToken: ct);
        Assert.Equal(1, potionConsume.Quantity);
        var inventory = await gamecoin.Inventory.ListAsync(player, ct);
        Assert.Equal(1, Assert.Single(inventory, i => i.Sku == "potion").Quantity);

        // purchase a potion with gems
        var purchase = await gamecoin.Purchases.CreateAsync(player, new PurchaseRequest { Sku = "potion" }, cancellationToken: ct);
        Assert.Equal(50, purchase.Balance.Total); // 70 - 20
        Assert.Equal(2, purchase.Item.Quantity);
        Assert.Equal(LedgerEntryType.Spend, purchase.Entry.Type);
        Assert.Equal("potion", purchase.Entry.Ref.ItemSku);
        Assert.False(purchase.Replayed);

        // a purchase beyond the wallet, and beyond maxOwned
        var tooPricey = await Assert.ThrowsAsync<GameCoinException>(() =>
            gamecoin.Purchases.CreateAsync(player, new PurchaseRequest { Sku = "fire-sword" }, cancellationToken: ct));
        Assert.Equal(GameCoinErrorCodes.InsufficientFunds, tooPricey.Code);

        // checkout of gems-500
        var checkout = await gamecoin.Checkouts.CreateAsync(player, new CheckoutRequest { PackSku = "gems-500", Country = "BE" }, cancellationToken: ct);
        Assert.Equal(OrderStatus.Pending, checkout.Order.Status);
        Assert.False(string.IsNullOrEmpty(checkout.CheckoutUrl));
        Assert.Contains("/pay/" + checkout.Order.Id, checkout.CheckoutUrl);
        Assert.Equal("gems-500", checkout.Order.Pack.Sku);
        Assert.Equal(created.Id, checkout.Order.PlayerId);
        Assert.Equal("EUR", checkout.Order.Currency);
        Assert.Equal("BE", checkout.Order.Country);
        Assert.Equal(checkout.Order.AmountCents, checkout.Order.NetCents + checkout.Order.VatCents);
        Assert.Null(checkout.Order.PaidAt);
        Assert.False(checkout.Replayed);

        // orders.get
        var order = await gamecoin.Orders.GetAsync(checkout.Order.Id, ct);
        Assert.Equal(checkout.Order.Id, order.Id);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(checkout.Order.AmountCents, order.AmountCents);

        // refund of the unpaid order: CONFLICT
        var refund = await Assert.ThrowsAsync<GameCoinException>(() => gamecoin.Orders.RefundAsync(checkout.Order.Id, new RefundRequest { Reason = "test" }, ct));
        Assert.Equal(GameCoinErrorCodes.Conflict, refund.Code);
        Assert.Equal(409, refund.Status);

        // unknown order, unknown player
        var noOrder = await Assert.ThrowsAsync<GameCoinException>(() => gamecoin.Orders.GetAsync("000000000000000000000000", ct));
        Assert.Equal(GameCoinErrorCodes.NotFound, noOrder.Code);
        var nobody = await Assert.ThrowsAsync<GameCoinException>(() => gamecoin.Players.GetAsync(PlayerRef.Ext("nobody-" + Guid.NewGuid().ToString("N")), ct));
        Assert.Equal(GameCoinErrorCodes.NotFound, nobody.Code);
        Assert.Equal(404, nobody.Status);
        Assert.DoesNotContain(SecretKey!, nobody.Message);
    }

    [IntegrationFact]
    public async Task Awkward_external_ids_round_trip_through_the_real_server()
    {
        using var gamecoin = new GameCoinClient(SecretKey!, new GameCoinOptions { BaseUrl = BaseUrl! });
        var ct = CancellationToken.None;
        var suffix = Guid.NewGuid().ToString("N");

        foreach (var awkward in new[] { "name with spaces ", "slash/inside", "percent%25sign", "café 日本", "plus+and?query#hash&amp" })
        {
            var externalId = awkward + "-" + suffix;
            var player = PlayerRef.Ext(externalId);

            var created = await gamecoin.Players.UpsertAsync(player, cancellationToken: ct);
            Assert.Equal(externalId, created.ExternalId);

            var granted = await gamecoin.Wallet.GrantAsync(player, new GrantRequest { Currency = "gold", Amount = 1 }, cancellationToken: ct);
            Assert.Equal(1, granted.Balance.Total);
            Assert.Equal(created.Id, (await gamecoin.Players.GetAsync(player, ct)).Id);
        }
    }

    [IntegrationFact]
    public async Task Non_ascii_text_and_quotes_survive_the_json_round_trip()
    {
        using var gamecoin = new GameCoinClient(SecretKey!, new GameCoinOptions { BaseUrl = BaseUrl! });
        var player = PlayerRef.Ext("sdk-dotnet-text-" + Guid.NewGuid().ToString("N"));
        const string reason = "récompense 日本 \"quoted\" \\ and \U0001F3AE";

        var granted = await gamecoin.Wallet.GrantAsync(
            player,
            new GrantRequest { Currency = "gold", Amount = 5, Reason = reason, Metadata = new Dictionary<string, string> { ["note"] = "café ✓" } });

        Assert.Equal(reason, granted.Entry.Reason);
        Assert.Equal("café ✓", granted.Entry.Metadata["note"]);
        var page = await gamecoin.Ledger.ListAsync(player);
        Assert.Equal(reason, Assert.Single(page.Entries).Reason);
    }

    [IntegrationFact]
    public async Task A_player_profile_can_be_set_then_cleared()
    {
        using var gamecoin = new GameCoinClient(SecretKey!, new GameCoinOptions { BaseUrl = BaseUrl! });
        var ct = CancellationToken.None;
        var player = PlayerRef.Ext("sdk-dotnet-profile-" + Guid.NewGuid().ToString("N"));

        var set = await gamecoin.Players.UpsertAsync(player, new PlayerProfile { DisplayName = "Ann", Country = "BE" }, ct);
        Assert.Equal("Ann", set.DisplayName);
        Assert.Equal("BE", set.Country);

        var unchanged = await gamecoin.Players.UpsertAsync(player, cancellationToken: ct);
        Assert.Equal("Ann", unchanged.DisplayName); // an empty profile changes nothing

        var cleared = await gamecoin.Players.UpsertAsync(player, new PlayerProfile { Clear = PlayerProfileFields.DisplayName }, ct);
        Assert.Null(cleared.DisplayName);
        Assert.Equal("BE", cleared.Country);
    }

    [IntegrationFact]
    public async Task A_wrong_key_is_unauthenticated()
    {
        using var gamecoin = new GameCoinClient("gc_sk_test_definitelyNotARealKey", new GameCoinOptions { BaseUrl = BaseUrl! });

        var error = await Assert.ThrowsAsync<GameCoinException>(() => gamecoin.Catalog.GetAsync(CancellationToken.None));

        Assert.Equal(GameCoinErrorCodes.Unauthenticated, error.Code);
        Assert.Equal(401, error.Status);
    }

    [IntegrationFact]
    public async Task Server_side_validation_errors_carry_field_errors()
    {
        using var gamecoin = new GameCoinClient(SecretKey!, new GameCoinOptions { BaseUrl = BaseUrl! });
        var ct = CancellationToken.None;

        // 99999999999999 is above the maximum amount of the API.
        var error = await Assert.ThrowsAsync<GameCoinException>(() =>
            gamecoin.Wallet.GrantAsync(PlayerRef.Ext("sdk-dotnet-v-" + Guid.NewGuid().ToString("N")), new GrantRequest { Currency = "gems", Amount = 99_999_999_999_999 }, cancellationToken: ct));

        Assert.Equal(GameCoinErrorCodes.ValidationFailed, error.Code);
        Assert.Equal(400, error.Status);
        Assert.True(error.Details.ContainsKey("fieldErrors"));
    }
}
