using System.Net.Http;
using System.Text.Json;

namespace Apilow.GameCoin.Tests;

/// <summary>What each method sends: verb, path, query, body, headers.</summary>
public class RequestTests
{
    private static readonly PlayerRef User = PlayerRef.Ext("user-42");

    [Fact]
    public async Task Catalog_get()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Catalog);

        await rig.Client.Catalog.GetAsync();

        Assert.Equal(HttpMethod.Get, rig.Only.Method);
        Assert.Equal("/api/v1/catalog", rig.Only.PathAndQuery);
        Assert.Equal(string.Empty, rig.Only.Body);
    }

    [Fact]
    public async Task Players_upsert_without_a_profile_sends_an_empty_object()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Player);

        await rig.Client.Players.UpsertAsync(User);

        Assert.Equal(HttpMethod.Put, rig.Only.Method);
        Assert.Equal("/api/v1/players/ext%3Auser-42", rig.Only.PathAndQuery);
        Assert.Equal("{}", rig.Only.Body);
        Assert.StartsWith("application/json", rig.Only.Header("Content-Type"));
        Assert.False(rig.Only.HasHeader("Idempotency-Key"));
    }

    [Fact]
    public async Task Players_upsert_sends_only_the_fields_that_are_set()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Player);

        await rig.Client.Players.UpsertAsync(User, new PlayerProfile { DisplayName = "Ann", Country = "BE", BirthYear = 1990 });

        Assert.Equal("""{"displayName":"Ann","country":"BE","birthYear":1990}""", rig.Only.Body);
    }

    [Fact]
    public async Task Players_upsert_sends_null_for_the_fields_to_clear()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Player);

        await rig.Client.Players.UpsertAsync(
            User,
            new PlayerProfile { DisplayName = "Ann", Clear = PlayerProfileFields.Email | PlayerProfileFields.BirthYear });

        Assert.Equal("""{"displayName":"Ann","email":null,"birthYear":null}""", rig.Only.Body);
    }

    [Fact]
    public async Task Players_upsert_rejects_a_field_both_set_and_cleared()
    {
        var rig = new TestRig();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            rig.Client.Players.UpsertAsync(User, new PlayerProfile { Email = "a@b.test", Clear = PlayerProfileFields.Email }));
        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public async Task Players_get_and_create_token()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Player).Ok("""{"token":"gc_pt_abc","expiresAt":"2026-10-05T21:38:32.000Z"}""");

        await rig.Client.Players.GetAsync(User);
        await rig.Client.Players.CreateTokenAsync(User);

        Assert.Equal(HttpMethod.Get, rig.Handler.Requests[0].Method);
        Assert.Equal("/api/v1/players/ext%3Auser-42", rig.Handler.Requests[0].PathAndQuery);
        Assert.Equal(HttpMethod.Post, rig.Handler.Requests[1].Method);
        Assert.Equal("/api/v1/players/ext%3Auser-42/tokens", rig.Handler.Requests[1].PathAndQuery);
        Assert.Equal(string.Empty, rig.Handler.Requests[1].Body);
        Assert.False(rig.Handler.Requests[1].HasHeader("Idempotency-Key"));
    }

    [Fact]
    public async Task Wallet_get()
    {
        var rig = new TestRig();
        rig.Handler.Ok("""{"playerId":"p1","balances":[]}""");

        await rig.Client.Wallet.GetAsync(User);

        Assert.Equal(HttpMethod.Get, rig.Only.Method);
        Assert.Equal("/api/v1/players/ext%3Auser-42/wallet", rig.Only.PathAndQuery);
    }

    [Fact]
    public async Task Wallet_grant_body_with_reason_and_metadata()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Movement);

        await rig.Client.Wallet.GrantAsync(
            User,
            new GrantRequest
            {
                Currency = "gems",
                Amount = 100,
                Reason = "level_up",
                Metadata = new Dictionary<string, string> { ["level"] = "7", ["quote"] = "a\"b" },
            });

        var request = rig.Only;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/v1/players/ext%3Auser-42/wallet/grant", request.PathAndQuery);
        var body = request.Json();
        Assert.Equal(new[] { "currency", "amount", "reason", "metadata" }, body.EnumerateObject().Select(p => p.Name));
        Assert.Equal("gems", body.GetProperty("currency").GetString());
        Assert.Equal(100, body.GetProperty("amount").GetInt64());
        Assert.Equal("level_up", body.GetProperty("reason").GetString());
        Assert.Equal("7", body.GetProperty("metadata").GetProperty("level").GetString());
        Assert.Equal("a\"b", body.GetProperty("metadata").GetProperty("quote").GetString());
        Assert.True(request.HasHeader("Idempotency-Key"));
    }

    [Fact]
    public async Task Wallet_grant_minimal_body_omits_optional_fields()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Movement);

        await rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 5 });

        Assert.Equal("""{"currency":"gems","amount":5}""", rig.Only.Body);
    }

    [Fact]
    public async Task Wallet_spend_body()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Movement);

        await rig.Client.Wallet.SpendAsync(User, new SpendRequest { Currency = "gold", Amount = 30, Reason = "shop" });

        Assert.Equal(HttpMethod.Post, rig.Only.Method);
        Assert.Equal("/api/v1/players/ext%3Auser-42/wallet/spend", rig.Only.PathAndQuery);
        Assert.Equal("""{"currency":"gold","amount":30,"reason":"shop"}""", rig.Only.Body);
        Assert.True(rig.Only.HasHeader("Idempotency-Key"));
    }

    [Fact]
    public async Task Large_amounts_are_sent_as_exact_integers()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Movement);

        await rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 9_007_199_254_740_993 });

        Assert.Contains("\"amount\":9007199254740993", rig.Only.Body);
    }

    [Fact]
    public async Task Ledger_list_without_a_query_has_no_query_string()
    {
        var rig = new TestRig();
        rig.Handler.Ok("""{"entries":[],"nextCursor":null}""");

        await rig.Client.Ledger.ListAsync(User);

        Assert.Equal(HttpMethod.Get, rig.Only.Method);
        Assert.Equal("/api/v1/players/ext%3Auser-42/ledger", rig.Only.PathAndQuery);
    }

    [Fact]
    public async Task Ledger_list_serializes_currency_limit_and_cursor()
    {
        var rig = new TestRig();
        rig.Handler.Ok("""{"entries":[],"nextCursor":null}""").Ok("""{"entries":[],"nextCursor":null}""");

        await rig.Client.Ledger.ListAsync(User, new LedgerQuery { Currency = "gems", Limit = 50, Cursor = "a+b/c=d e" });
        await rig.Client.Ledger.ListAsync(User, new LedgerQuery { Limit = 1 });

        Assert.Equal("/api/v1/players/ext%3Auser-42/ledger?currency=gems&limit=50&cursor=a%2Bb%2Fc%3Dd%20e", rig.Handler.Requests[0].PathAndQuery);
        Assert.Equal("/api/v1/players/ext%3Auser-42/ledger?limit=1", rig.Handler.Requests[1].PathAndQuery);
    }

    [Fact]
    public async Task Inventory_list_grant_consume()
    {
        var rig = new TestRig();
        rig.Handler.Ok("""{"items":[]}""").Ok($$"""{"item":{{Samples.Item}}}""").Ok($$"""{"item":{{Samples.Item}}}""");

        await rig.Client.Inventory.ListAsync(User);
        await rig.Client.Inventory.GrantAsync(User, new InventoryGrantRequest { Sku = "potion", Quantity = 3 });
        await rig.Client.Inventory.ConsumeAsync(User, new InventoryConsumeRequest { Sku = "potion" });

        var list = rig.Handler.Requests[0];
        Assert.Equal(HttpMethod.Get, list.Method);
        Assert.Equal("/api/v1/players/ext%3Auser-42/inventory", list.PathAndQuery);
        Assert.False(list.HasHeader("Idempotency-Key"));

        var grant = rig.Handler.Requests[1];
        Assert.Equal(HttpMethod.Post, grant.Method);
        Assert.Equal("/api/v1/players/ext%3Auser-42/inventory/grant", grant.PathAndQuery);
        Assert.Equal("""{"sku":"potion","quantity":3}""", grant.Body);
        Assert.True(grant.HasHeader("Idempotency-Key"));

        var consume = rig.Handler.Requests[2];
        Assert.Equal("/api/v1/players/ext%3Auser-42/inventory/consume", consume.PathAndQuery);
        Assert.Equal("""{"sku":"potion","quantity":1}""", consume.Body); // quantity defaults to 1
        Assert.True(consume.HasHeader("Idempotency-Key"));
    }

    [Fact]
    public async Task Purchases_create()
    {
        var rig = new TestRig();
        rig.Handler.Ok($$"""{"entry":{{Samples.Entry}},"balance":{{Samples.Balance}},"item":{{Samples.Item}}}""");

        await rig.Client.Purchases.CreateAsync(User, new PurchaseRequest { Sku = "potion", Quantity = 2 });

        Assert.Equal(HttpMethod.Post, rig.Only.Method);
        Assert.Equal("/api/v1/players/ext%3Auser-42/purchases", rig.Only.PathAndQuery);
        Assert.Equal("""{"sku":"potion","quantity":2}""", rig.Only.Body);
        Assert.True(rig.Only.HasHeader("Idempotency-Key"));
    }

    [Fact]
    public async Task Checkouts_create_with_every_field()
    {
        var rig = new TestRig();
        rig.Handler.Ok($$"""{"order":{{Samples.Order}},"checkoutUrl":"https://gc.example.test/pay/o1?t=x"}""");

        await rig.Client.Checkouts.CreateAsync(
            User,
            new CheckoutRequest { PackSku = "gems-500", SuccessUrl = "https://game.test/ok", CancelUrl = "https://game.test/ko", Country = "BE" });

        Assert.Equal(HttpMethod.Post, rig.Only.Method);
        Assert.Equal("/api/v1/players/ext%3Auser-42/checkouts", rig.Only.PathAndQuery);
        Assert.Equal(
            """{"packSku":"gems-500","successUrl":"https://game.test/ok","cancelUrl":"https://game.test/ko","country":"BE"}""",
            rig.Only.Body);
        Assert.True(rig.Only.HasHeader("Idempotency-Key"));
    }

    [Fact]
    public async Task Checkouts_create_with_only_the_pack()
    {
        var rig = new TestRig();
        rig.Handler.Ok($$"""{"order":{{Samples.Order}},"checkoutUrl":"u"}""");

        await rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter" });

        Assert.Equal("""{"packSku":"starter"}""", rig.Only.Body);
    }

    [Fact]
    public async Task Orders_get_and_refund()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Order).Ok(Samples.Order).Ok(Samples.Order);

        await rig.Client.Orders.GetAsync("o1");
        await rig.Client.Orders.RefundAsync("o1");
        await rig.Client.Orders.RefundAsync("o 1/2", new RefundRequest { Reason = "chargeback" });

        Assert.Equal(HttpMethod.Get, rig.Handler.Requests[0].Method);
        Assert.Equal("/api/v1/orders/o1", rig.Handler.Requests[0].PathAndQuery);

        Assert.Equal(HttpMethod.Post, rig.Handler.Requests[1].Method);
        Assert.Equal("/api/v1/orders/o1/refund", rig.Handler.Requests[1].PathAndQuery);
        Assert.Equal("{}", rig.Handler.Requests[1].Body);
        Assert.False(rig.Handler.Requests[1].HasHeader("Idempotency-Key")); // a refund has no idempotency key

        Assert.Equal("/api/v1/orders/o%201%2F2/refund", rig.Handler.Requests[2].PathAndQuery);
        Assert.Equal("""{"reason":"chargeback"}""", rig.Handler.Requests[2].Body);
    }

    // --- one URL encoding of the player reference ---------------------------------------------------------------------

    [Theory]
    [InlineData("user 42", "ext%3Auser%2042")]
    [InlineData("a/b", "ext%3Aa%2Fb")]
    [InlineData("100%", "ext%3A100%25")]
    [InlineData("100%25", "ext%3A100%2525")]
    [InlineData("café über 日本", "ext%3Acaf%C3%A9%20%C3%BCber%20%E6%97%A5%E6%9C%AC")]
    [InlineData("a+b?c#d&e=f", "ext%3Aa%2Bb%3Fc%23d%26e%3Df")]
    [InlineData("../..", "ext%3A..%2F..")]
    [InlineData("\U0001F3AE", "ext%3A%F0%9F%8E%AE")]
    public async Task Ext_ids_are_url_encoded_exactly_once(string externalId, string expectedSegment)
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Player);

        await rig.Client.Players.GetAsync(PlayerRef.Ext(externalId));

        Assert.Equal("/api/v1/players/" + expectedSegment, rig.Only.PathAndQuery);
        // and the server decodes it back to exactly what the game gave
        var decoded = Uri.UnescapeDataString(rig.Only.PathAndQuery.Substring("/api/v1/players/".Length));
        Assert.Equal("ext:" + externalId, decoded);
    }

    [Fact]
    public async Task Ext_ids_are_encoded_in_every_player_path()
    {
        var rig = new TestRig();
        rig.Handler
            .Ok(Samples.Player).Ok(Samples.Player).Ok("""{"token":"t","expiresAt":"2026-10-05T21:38:32.000Z"}""")
            .Ok("""{"playerId":"p","balances":[]}""").Ok(Samples.Movement).Ok(Samples.Movement)
            .Ok("""{"entries":[],"nextCursor":null}""").Ok("""{"items":[]}""")
            .Ok($$"""{"item":{{Samples.Item}}}""").Ok($$"""{"item":{{Samples.Item}}}""")
            .Ok($$"""{"entry":{{Samples.Entry}},"balance":{{Samples.Balance}},"item":{{Samples.Item}}}""")
            .Ok($$"""{"order":{{Samples.Order}},"checkoutUrl":"u"}""");
        var player = PlayerRef.Ext("a b/c%dé");

        await rig.Client.Players.UpsertAsync(player);
        await rig.Client.Players.GetAsync(player);
        await rig.Client.Players.CreateTokenAsync(player);
        await rig.Client.Wallet.GetAsync(player);
        await rig.Client.Wallet.GrantAsync(player, new GrantRequest { Currency = "gems", Amount = 1 });
        await rig.Client.Wallet.SpendAsync(player, new SpendRequest { Currency = "gems", Amount = 1 });
        await rig.Client.Ledger.ListAsync(player);
        await rig.Client.Inventory.ListAsync(player);
        await rig.Client.Inventory.GrantAsync(player, new InventoryGrantRequest { Sku = "potion" });
        await rig.Client.Inventory.ConsumeAsync(player, new InventoryConsumeRequest { Sku = "potion" });
        await rig.Client.Purchases.CreateAsync(player, new PurchaseRequest { Sku = "potion" });
        await rig.Client.Checkouts.CreateAsync(player, new CheckoutRequest { PackSku = "starter" });

        Assert.Equal(12, rig.Handler.Requests.Count);
        Assert.All(rig.Handler.Requests, r => Assert.StartsWith("/api/v1/players/ext%3Aa%20b%2Fc%25d%C3%A9", r.PathAndQuery));
    }

    [Fact]
    public async Task Raw_player_ids_are_encoded_too()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Player);

        await rig.Client.Players.GetAsync(PlayerRef.FromId("6ac40ac0"));

        Assert.Equal("/api/v1/players/6ac40ac0", rig.Only.PathAndQuery);
    }

    [Fact]
    public async Task A_plain_string_converts_to_a_player_id()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Player);

        await rig.Client.Players.GetAsync("6ac40ac0");

        Assert.Equal("/api/v1/players/6ac40ac0", rig.Only.PathAndQuery);
    }

    [Fact]
    public async Task Body_json_roundtrips_non_ascii_text()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Movement);

        await rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1, Reason = "café" });

        Assert.Equal("café", JsonDocument.Parse(rig.Only.Body).RootElement.GetProperty("reason").GetString());
        Assert.Equal("application/json; charset=utf-8", rig.Only.Header("Content-Type"));
    }
}
