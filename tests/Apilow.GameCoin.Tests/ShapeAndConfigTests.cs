using System.Net;

namespace Apilow.GameCoin.Tests;

/// <summary>A JSON body of an unexpected shape is INVALID_RESPONSE, whatever the call: the status is kept, no value is echoed.</summary>
public class ShapeTests
{
    private static readonly PlayerRef User = PlayerRef.Ext("user-42");

    public static IEnumerable<object[]> BrokenBodies()
    {
        // (name, call, body, expected path in the message)
        yield return Broken("player without id", rig => rig.Client.Players.GetAsync(User), Samples.Player.Replace("\"id\":\"6ac40ac02e9fe50981a1e085\",", ""), "player.id");
        yield return Broken("player with a numeric id", rig => rig.Client.Players.GetAsync(User), Samples.Player.Replace("\"6ac40ac02e9fe50981a1e085\"", "424242"), "player.id");
        yield return Broken("player without nullable externalId", rig => rig.Client.Players.GetAsync(User), Samples.Player.Replace("\"externalId\":\"probe-190\",", ""), "player.externalId");
        yield return Broken("player with a null kind", rig => rig.Client.Players.GetAsync(User), Samples.Player.Replace("\"kind\":\"external\"", "\"kind\":null"), "player.kind");
        yield return Broken("player with a text boolean", rig => rig.Client.Players.GetAsync(User), Samples.Player.Replace("\"blocked\":false", "\"blocked\":\"no\""), "player.blocked");
        yield return Broken("player with a bad date", rig => rig.Client.Players.GetAsync(User), Samples.Player.Replace("2026-10-05T20:38:24.539Z", "yesterday"), "player.createdAt");
        yield return Broken("token without expiry", rig => rig.Client.Players.CreateTokenAsync(User), """{"token":"gc_pt_x"}""", "response.expiresAt");
        yield return Broken("wallet without balances", rig => rig.Client.Wallet.GetAsync(User), """{"playerId":"p"}""", "wallet.balances");
        yield return Broken("wallet with a fractional amount", rig => rig.Client.Wallet.GetAsync(User), """{"playerId":"p","balances":[{"currency":"gems","paid":0,"bonus":1.5,"total":1}]}""", "wallet.balances[0].bonus");
        yield return Broken("movement without balance", rig => rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 }), $$"""{"entry":{{Samples.Entry}}}""", "response.balance");
        yield return Broken("movement entry without balanceAfter", rig => rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 }), $$"""{"entry":{{Samples.Entry.Replace("\"balanceAfter\":{\"paid\":0,\"bonus\":100,\"total\":100},", "")}},"balance":{{Samples.Balance}}}""", "response.entry.balanceAfter");
        yield return Broken("entry with a null metadata", rig => rig.Client.Wallet.SpendAsync(User, new SpendRequest { Currency = "gems", Amount = 1 }), $$"""{"entry":{{Samples.Entry.Replace("\"metadata\":{\"a\":\"b\"}", "\"metadata\":null")}},"balance":{{Samples.Balance}}}""", "response.entry.metadata");
        yield return Broken("entry with a non-string metadata value", rig => rig.Client.Wallet.SpendAsync(User, new SpendRequest { Currency = "gems", Amount = 1 }), $$"""{"entry":{{Samples.Entry.Replace("\"a\":\"b\"", "\"a\":7")}},"balance":{{Samples.Balance}}}""", "response.entry.metadata.a");
        yield return Broken("entry with a null ref item", rig => rig.Client.Wallet.SpendAsync(User, new SpendRequest { Currency = "gems", Amount = 1 }), $$"""{"entry":{{Samples.Entry.Replace("\"ref\":{}", "\"ref\":{\"itemSku\":null}")}},"balance":{{Samples.Balance}}}""", "response.entry.ref.itemSku");
        yield return Broken("ledger page without a cursor key", rig => rig.Client.Ledger.ListAsync(User), """{"entries":[]}""", "response.nextCursor");
        yield return Broken("ledger page with a broken entry", rig => rig.Client.Ledger.ListAsync(User), $$"""{"entries":[{{Samples.Entry}},{"id":"x"}],"nextCursor":null}""", "response.entries[1].balanceAfter");
        yield return Broken("inventory without items", rig => rig.Client.Inventory.ListAsync(User), """{}""", "response.items");
        yield return Broken("inventory change without item", rig => rig.Client.Inventory.GrantAsync(User, new InventoryGrantRequest { Sku = "potion" }), """{"quantity":1}""", "response.item");
        yield return Broken("purchase without item", rig => rig.Client.Purchases.CreateAsync(User, new PurchaseRequest { Sku = "potion" }), $$"""{"entry":{{Samples.Entry}},"balance":{{Samples.Balance}}}""", "response.item");
        yield return Broken("checkout without url", rig => rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter" }), $$"""{"order":{{Samples.Order}}}""", "response.checkoutUrl");
        yield return Broken("checkout with a broken order", rig => rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter" }), """{"order":{"id":"o"},"checkoutUrl":"u"}""", "response.order.playerId");
        yield return Broken("order without pack", rig => rig.Client.Orders.GetAsync("o1"), Samples.Order.Replace("\"pack\":{\"sku\":\"gems-500\",\"name\":\"Bag of gems\"},", ""), "order.pack");
        yield return Broken("order without paidAt", rig => rig.Client.Orders.GetAsync("o1"), Samples.Order.Replace("\"paidAt\":null,", ""), "order.paidAt");
        yield return Broken("order with a text amount", rig => rig.Client.Orders.RefundAsync("o1"), Samples.Order.Replace("\"amountCents\":499", "\"amountCents\":\"499\""), "order.amountCents");
        yield return Broken("catalog without packs", rig => rig.Client.Catalog.GetAsync(), """{"currencies":[],"items":[]}""", "catalog.packs");
        yield return Broken("catalog item without type", rig => rig.Client.Catalog.GetAsync(), Samples.Catalog.Replace("\"type\":\"consumable\",", ""), "catalog.items[0].type");
        yield return Broken("catalog pack with a bad grant", rig => rig.Client.Catalog.GetAsync(), Samples.Catalog.Replace("\"amount\":100,\"bonus\":20", "\"amount\":100"), "catalog.packs[0].grants[0].bonus");
        yield return Broken("catalog item with a broken price", rig => rig.Client.Catalog.GetAsync(), Samples.Catalog.Replace("\"price\":{\"currency\":\"gems\",\"amount\":20}", "\"price\":{\"currency\":\"gems\"}"), "catalog.items[0].price.amount");

        // 0.3: gift codes, creator code, availability, prelaunch
        yield return Broken("redemption without grants", rig => rig.Client.Codes.RedeemAsync(User, "C"), """{"items":[],"skipped":[]}""", "response.grants");
        yield return Broken("redemption without skipped", rig => rig.Client.Codes.RedeemAsync(User, "C"), """{"grants":[],"items":[]}""", "response.skipped");
        yield return Broken("redemption grant without balance", rig => rig.Client.Codes.RedeemAsync(User, "C"), """{"grants":[{"currency":"gems","amount":5}],"items":[],"skipped":[]}""", "response.grants[0].balance");
        yield return Broken("redemption grant with a text amount", rig => rig.Client.Codes.RedeemAsync(User, "C"), $$"""{"grants":[{"currency":"gems","amount":"5","balance":{{Samples.Balance}}}],"items":[],"skipped":[]}""", "response.grants[0].amount");
        yield return Broken("redemption item without quantity", rig => rig.Client.Codes.RedeemAsync(User, "C"), """{"grants":[],"items":[{"sku":"potion"}],"skipped":[]}""", "response.items[0].quantity");
        yield return Broken("redemption skipped item without sku", rig => rig.Client.Codes.RedeemAsync(User, "C"), """{"grants":[],"items":[],"skipped":[{"quantity":1}]}""", "response.skipped[0].sku");
        yield return Broken("order creator code that is not an object", rig => rig.Client.Orders.GetAsync("o1"), Samples.Order.Replace("\"creatorCode\":null", "\"creatorCode\":\"STREAMER10\""), "order.creatorCode");
        yield return Broken("order creator code without a name", rig => rig.Client.Orders.GetAsync("o1"), Samples.Order.Replace("\"creatorCode\":null", "\"creatorCode\":{\"code\":\"X\"}"), "order.creatorCode.creatorName");
        yield return Broken("order creator code with a numeric code", rig => rig.Client.Orders.GetAsync("o1"), Samples.Order.Replace("\"creatorCode\":null", "\"creatorCode\":{\"code\":7,\"creatorName\":\"A\"}"), "order.creatorCode.code");
        yield return Broken("checkout whose order has a broken creator code", rig => rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter" }), $$"""{"order":{{Samples.Order.Replace("\"creatorCode\":null", "\"creatorCode\":[]")}},"checkoutUrl":"u"}""", "response.order.creatorCode");
        yield return Broken("pack availability that is not an object", rig => rig.Client.Catalog.GetAsync(), Samples.Catalog.Replace("\"availability\":null", "\"availability\":true"), "catalog.packs[0].availability");
        yield return Broken("pack availability without founder", rig => rig.Client.Catalog.GetAsync(), Samples.Catalog.Replace("\"availability\":null", "\"availability\":{\"startsAt\":null,\"endsAt\":null,\"remaining\":null}"), "catalog.packs[0].availability.founder");
        yield return Broken("pack availability without remaining", rig => rig.Client.Catalog.GetAsync(), Samples.Catalog.Replace("\"availability\":null", "\"availability\":{\"startsAt\":null,\"endsAt\":null,\"founder\":false}"), "catalog.packs[0].availability.remaining");
        yield return Broken("pack availability with a fractional stock", rig => rig.Client.Catalog.GetAsync(), Samples.Catalog.Replace("\"availability\":null", "\"availability\":{\"startsAt\":null,\"endsAt\":null,\"remaining\":1.5,\"founder\":false}"), "catalog.packs[0].availability.remaining");
        yield return Broken("pack availability with a bad start", rig => rig.Client.Catalog.GetAsync(), Samples.Catalog.Replace("\"availability\":null", "\"availability\":{\"startsAt\":\"soon\",\"endsAt\":null,\"remaining\":null,\"founder\":false}"), "catalog.packs[0].availability.startsAt");
        yield return Broken("pack availability without an end key", rig => rig.Client.Catalog.GetAsync(), Samples.Catalog.Replace("\"availability\":null", "\"availability\":{\"startsAt\":null,\"remaining\":null,\"founder\":false}"), "catalog.packs[0].availability.endsAt");
        yield return Broken("catalog with a text prelaunch", rig => rig.Client.Catalog.GetAsync(), Samples.Catalog.Replace("\"prelaunch\":false", "\"prelaunch\":\"no\""), "catalog.prelaunch");
        yield return Broken("catalog with a null prelaunch", rig => rig.Client.Catalog.GetAsync(), Samples.Catalog.Replace("\"prelaunch\":false", "\"prelaunch\":null"), "catalog.prelaunch");
        yield return Broken("catalog with a bad launch date", rig => rig.Client.Catalog.GetAsync(), Samples.Catalog.Replace("\"launchAt\":null", "\"launchAt\":\"tomorrow\""), "catalog.launchAt");
    }

    private static object[] Broken(string name, Func<TestRig, Task> call, string body, string path) => new object[] { name, call, body, path };

    [Theory]
    [MemberData(nameof(BrokenBodies))]
    public async Task A_body_of_the_wrong_shape_is_INVALID_RESPONSE_with_the_http_status_and_the_path(string name, Func<TestRig, Task> call, string body, string path)
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Ok(body);

        var error = await Assert.ThrowsAsync<GameCoinException>(() => call(rig));

        Assert.Equal(GameCoinErrorCodes.InvalidResponse, error.Code);
        Assert.Equal(200, error.Status);
        Assert.Contains(path + " must be", error.Message);
        Assert.Single(rig.Handler.Requests); // a malformed 200 is never retried
        Assert.True(name.Length > 0);
    }

    [Fact]
    public async Task The_message_never_echoes_the_offending_value()
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Ok(Samples.Player.Replace("\"6ac40ac02e9fe50981a1e085\"", "7777777777"));

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Players.GetAsync(User));

        Assert.DoesNotContain("7777777777", error.Message);
    }

    [Fact]
    public async Task Unknown_fields_and_unknown_enumeration_values_are_fine()
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Ok(Samples.Order.Replace("\"status\":\"pending\"", "\"status\":\"disputed_v2\"").Replace("{\"id\":\"o1\"", "{\"brandNew\":[1,2],\"id\":\"o1\""));

        var order = await rig.Client.Orders.GetAsync("o1");

        Assert.Equal("disputed_v2", order.Status);
    }

    [Fact]
    public async Task A_5xx_html_page_is_retried_but_a_malformed_200_is_not()
    {
        var rig = new TestRig(maxRetries: 2);
        rig.Handler.Raw(HttpStatusCode.BadGateway, "<html>gateway</html>").Ok("""{"playerId":"p"}""");

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Wallet.GetAsync(User));

        Assert.Equal(2, rig.Handler.Requests.Count);
        Assert.Equal(GameCoinErrorCodes.InvalidResponse, error.Code);
        Assert.Equal(200, error.Status);
    }
}

public class ConfigAlignmentTests
{
    [Fact]
    public async Task The_secret_key_is_trimmed()
    {
        var handler = new FakeHandler().Ok(Samples.Catalog);
        using var client = new GameCoinClient("  gc_sk_test_abc123\r\n", new GameCoinOptions { BaseUrl = "https://gc.example.test", HttpMessageHandler = handler });

        await client.Catalog.GetAsync();

        Assert.Equal("Bearer gc_sk_test_abc123", handler.Requests[0].Header("Authorization"));
        Assert.Equal("test", client.Environment);
    }

    [Fact]
    public void The_client_shows_the_key_prefix_only()
    {
        using var client = new GameCoinClient("gc_sk_live_abcdefghijkl");

        var text = client.ToString();

        Assert.Contains("gc_sk_live_…", text);
        Assert.DoesNotContain("abcdefghijkl", text);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    [InlineData(-1, false)]
    public void MaxRetries_is_bounded_from_0_to_10(int maxRetries, bool accepted)
    {
        Func<GameCoinClient> create = () => new GameCoinClient("gc_sk_test_abc", new GameCoinOptions { MaxRetries = maxRetries });
        if (accepted)
        {
            create().Dispose();
        }
        else
        {
            Assert.Throws<ArgumentException>(create);
        }
    }

    [Theory]
    [InlineData("https://gc.example.test/?a=b")]
    [InlineData("https://gc.example.test/#frag")]
    [InlineData("https://user:pass@gc.example.test/")]
    [InlineData("https://user@gc.example.test/")]
    public void The_base_url_has_no_credentials_query_or_fragment(string baseUrl)
    {
        var error = Assert.Throws<ArgumentException>(() => new GameCoinClient("gc_sk_test_abc", new GameCoinOptions { BaseUrl = baseUrl }));
        Assert.DoesNotContain("pass", error.Message);
    }

    [Theory]
    [InlineData("https://GC.example.test:443/", "https://gc.example.test")]
    [InlineData("http://localhost:3400/gateway//", "http://localhost:3400/gateway")]
    [InlineData("  https://gc.example.test/a/b/  ", "https://gc.example.test/a/b")]
    public void The_base_url_is_reduced_to_the_origin_plus_an_optional_path_prefix(string baseUrl, string expected)
    {
        using var client = new GameCoinClient("gc_sk_test_abc", new GameCoinOptions { BaseUrl = baseUrl });
        Assert.Equal(expected, client.BaseUrl);
    }

    [Fact]
    public async Task Whitespace_only_ids_are_the_server_s_business_only_empty_ones_are_rejected_locally()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Player);

        await rig.Client.Players.GetAsync(PlayerRef.Ext(" "));

        Assert.Equal("/api/v1/players/ext%3A%20", rig.Only.PathAndQuery);
    }
}
