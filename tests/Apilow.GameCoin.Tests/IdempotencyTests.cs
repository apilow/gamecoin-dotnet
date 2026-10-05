using System.Text.RegularExpressions;

namespace Apilow.GameCoin.Tests;

public class IdempotencyTests
{
    private static readonly PlayerRef User = PlayerRef.Ext("user-42");
    private static readonly Regex UuidV4 = new("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");

    /// <summary>Every idempotent call, as a delegate over a rig whose handler already holds one successful reply.</summary>
    public static IEnumerable<object[]> IdempotentCalls()
    {
        yield return Call("wallet.grant", rig => rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 }), Samples.Movement);
        yield return Call("wallet.spend", rig => rig.Client.Wallet.SpendAsync(User, new SpendRequest { Currency = "gems", Amount = 1 }), Samples.Movement);
        yield return Call("inventory.grant", rig => rig.Client.Inventory.GrantAsync(User, new InventoryGrantRequest { Sku = "potion" }), $$"""{"item":{{Samples.Item}}}""");
        yield return Call("inventory.consume", rig => rig.Client.Inventory.ConsumeAsync(User, new InventoryConsumeRequest { Sku = "potion" }), $$"""{"item":{{Samples.Item}}}""");
        yield return Call("purchases.create", rig => rig.Client.Purchases.CreateAsync(User, new PurchaseRequest { Sku = "potion" }), $$"""{"entry":{{Samples.Entry}},"balance":{{Samples.Balance}},"item":{{Samples.Item}}}""");
        yield return Call("checkouts.create", rig => rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter" }), $$"""{"order":{{Samples.Order}},"checkoutUrl":"u"}""");
    }

    /// <summary>The same calls with a caller-supplied key.</summary>
    public static IEnumerable<object[]> IdempotentCallsWithKey()
    {
        const string key = "my-own-key-001";
        var options = new RequestOptions { IdempotencyKey = key };
        yield return Call("wallet.grant", rig => rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 }, options), Samples.Movement);
        yield return Call("wallet.spend", rig => rig.Client.Wallet.SpendAsync(User, new SpendRequest { Currency = "gems", Amount = 1 }, options), Samples.Movement);
        yield return Call("inventory.grant", rig => rig.Client.Inventory.GrantAsync(User, new InventoryGrantRequest { Sku = "potion" }, options), $$"""{"item":{{Samples.Item}}}""");
        yield return Call("inventory.consume", rig => rig.Client.Inventory.ConsumeAsync(User, new InventoryConsumeRequest { Sku = "potion" }, options), $$"""{"item":{{Samples.Item}}}""");
        yield return Call("purchases.create", rig => rig.Client.Purchases.CreateAsync(User, new PurchaseRequest { Sku = "potion" }, options), $$"""{"entry":{{Samples.Entry}},"balance":{{Samples.Balance}},"item":{{Samples.Item}}}""");
        yield return Call("checkouts.create", rig => rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter" }, options), $$"""{"order":{{Samples.Order}},"checkoutUrl":"u"}""");
    }

    private static object[] Call(string name, Func<TestRig, Task> call, string okJson) => new object[] { name, call, okJson };

    [Theory]
    [MemberData(nameof(IdempotentCalls))]
    public async Task Every_idempotent_call_sends_a_generated_uuid_v4_key(string name, Func<TestRig, Task> call, string okJson)
    {
        var rig = new TestRig();
        rig.Handler.Ok(okJson);

        await call(rig);

        var key = rig.Only.Header("Idempotency-Key");
        Assert.Matches(UuidV4, key);
        Assert.True(name.Length > 0);
    }

    [Theory]
    [MemberData(nameof(IdempotentCalls))]
    public async Task The_generated_key_is_kept_across_retries_and_new_for_each_call(string name, Func<TestRig, Task> call, string okJson)
    {
        var rig = new TestRig();
        rig.Handler
            .Error(System.Net.HttpStatusCode.ServiceUnavailable, "INTERNAL", "down")
            .Throw(new HttpRequestException("reset"))
            .Ok(okJson)
            .Ok(okJson);

        await call(rig); // 3 attempts
        await call(rig); // a second, distinct call

        Assert.Equal(4, rig.Handler.Requests.Count);
        var first = rig.Handler.Requests[0].Header("Idempotency-Key");
        Assert.Matches(UuidV4, first);
        Assert.Equal(first, rig.Handler.Requests[1].Header("Idempotency-Key"));
        Assert.Equal(first, rig.Handler.Requests[2].Header("Idempotency-Key"));
        var second = rig.Handler.Requests[3].Header("Idempotency-Key");
        Assert.Matches(UuidV4, second);
        Assert.NotEqual(first, second);
        Assert.True(name.Length > 0);
    }

    [Theory]
    [MemberData(nameof(IdempotentCallsWithKey))]
    public async Task The_caller_can_supply_the_key_and_it_survives_retries(string name, Func<TestRig, Task> call, string okJson)
    {
        var rig = new TestRig();
        rig.Handler.Error(System.Net.HttpStatusCode.BadGateway, "INTERNAL", "down").Ok(okJson);

        await call(rig);

        Assert.Equal(2, rig.Handler.Requests.Count);
        Assert.All(rig.Handler.Requests, r => Assert.Equal("my-own-key-001", r.Header("Idempotency-Key")));
        Assert.True(name.Length > 0);
    }

    [Fact]
    public async Task Reads_never_carry_an_idempotency_key()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Catalog).Ok(Samples.Order);

        await rig.Client.Catalog.GetAsync();
        await rig.Client.Orders.GetAsync("o1");

        Assert.All(rig.Handler.Requests, r => Assert.False(r.HasHeader("Idempotency-Key")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("café")]
    [InlineData("tab\there")]
    [InlineData("line\nbreak")]
    public async Task An_invalid_custom_key_is_rejected_before_any_request(string key)
    {
        var rig = new TestRig();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 }, new RequestOptions { IdempotencyKey = key }));

        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public async Task A_key_of_101_characters_is_rejected_and_100_is_accepted()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Movement);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 }, new RequestOptions { IdempotencyKey = new string('k', 101) }));
        Assert.Empty(rig.Handler.Requests);

        await rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 }, new RequestOptions { IdempotencyKey = "with space ~!" + new string('k', 87) });
        Assert.Equal(100, rig.Only.Header("Idempotency-Key").Length);
    }

    [Fact]
    public async Task Replayed_is_true_only_when_the_api_says_so()
    {
        var rig = new TestRig();
        rig.Handler
            .Ok(Samples.Movement)
            .Ok(Samples.Movement, ("Idempotent-Replayed", "true"))
            .Ok(Samples.Movement, ("Idempotent-Replayed", "false"));

        var fresh = await rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 });
        var replay = await rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 });
        var other = await rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 });

        Assert.False(fresh.Replayed);
        Assert.True(replay.Replayed);
        Assert.False(other.Replayed);
        Assert.Equal(fresh.Entry.Id, replay.Entry.Id); // same body, only the flag differs
    }

    [Fact]
    public async Task Replayed_is_exposed_by_each_idempotent_result_type()
    {
        var rig = new TestRig();
        var replayed = ("Idempotent-Replayed", "true");
        rig.Handler
            .Ok($$"""{"item":{{Samples.Item}}}""", replayed)
            .Ok($$"""{"item":{{Samples.Item}}}""", replayed)
            .Ok($$"""{"entry":{{Samples.Entry}},"balance":{{Samples.Balance}},"item":{{Samples.Item}}}""", replayed)
            .Ok($$"""{"order":{{Samples.Order}},"checkoutUrl":"u"}""", replayed)
            .Ok("""{"items":[]}""");

        Assert.True((await rig.Client.Inventory.GrantAsync(User, new InventoryGrantRequest { Sku = "potion" })).Replayed);
        Assert.True((await rig.Client.Inventory.ConsumeAsync(User, new InventoryConsumeRequest { Sku = "potion" })).Replayed);
        Assert.True((await rig.Client.Purchases.CreateAsync(User, new PurchaseRequest { Sku = "potion" })).Replayed);
        Assert.True((await rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter" })).Replayed);
        Assert.Empty(await rig.Client.Inventory.ListAsync(User));
    }
}
