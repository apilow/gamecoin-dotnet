using System.Net;
using System.Reflection;

namespace Apilow.GameCoin.Tests;

/// <summary>Bad arguments never cost a request; the secret key never leaves in a message or a string.</summary>
public class ArgumentAndSecretTests
{
    private static readonly PlayerRef User = PlayerRef.Ext("user-42");

    public static IEnumerable<object[]> BadCalls()
    {
        PlayerRef none = default;
        yield return Bad("default player", rig => rig.Client.Wallet.GetAsync(none));
        yield return Bad("default player on players.get", rig => rig.Client.Players.GetAsync(none));
        yield return Bad("default player on upsert", rig => rig.Client.Players.UpsertAsync(none));
        yield return Bad("default player on token", rig => rig.Client.Players.CreateTokenAsync(none));
        yield return Bad("default player on ledger", rig => rig.Client.Ledger.ListAsync(none));
        yield return Bad("default player on inventory", rig => rig.Client.Inventory.ListAsync(none));
        yield return Bad("grant: amount 0", rig => rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 0 }));
        yield return Bad("grant: negative amount", rig => rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = -5 }));
        yield return Bad("grant: empty currency", rig => rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "", Amount = 5 }));
        yield return Bad("grant: unset currency", rig => rig.Client.Wallet.GrantAsync(User, new GrantRequest { Amount = 5 }));
        yield return Bad("grant: null request", rig => rig.Client.Wallet.GrantAsync(User, null!));
        yield return Bad("spend: negative amount", rig => rig.Client.Wallet.SpendAsync(User, new SpendRequest { Currency = "gems", Amount = -1 }));
        yield return Bad("spend: default player", rig => rig.Client.Wallet.SpendAsync(none, new SpendRequest { Currency = "gems", Amount = 1 }));
        yield return Bad("inventory.grant: quantity 0", rig => rig.Client.Inventory.GrantAsync(User, new InventoryGrantRequest { Sku = "potion", Quantity = 0 }));
        yield return Bad("inventory.grant: negative quantity", rig => rig.Client.Inventory.GrantAsync(User, new InventoryGrantRequest { Sku = "potion", Quantity = -2 }));
        yield return Bad("inventory.grant: empty sku", rig => rig.Client.Inventory.GrantAsync(User, new InventoryGrantRequest { Sku = "" }));
        yield return Bad("inventory.consume: quantity 0", rig => rig.Client.Inventory.ConsumeAsync(User, new InventoryConsumeRequest { Sku = "potion", Quantity = 0 }));
        yield return Bad("inventory.consume: empty sku", rig => rig.Client.Inventory.ConsumeAsync(User, new InventoryConsumeRequest()));
        yield return Bad("purchase: quantity 0", rig => rig.Client.Purchases.CreateAsync(User, new PurchaseRequest { Sku = "potion", Quantity = 0 }));
        yield return Bad("purchase: empty sku", rig => rig.Client.Purchases.CreateAsync(User, new PurchaseRequest { Sku = "" }));
        yield return Bad("purchase: null request", rig => rig.Client.Purchases.CreateAsync(User, null!));
        yield return Bad("checkout: empty pack", rig => rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest()));
        yield return Bad("checkout: null request", rig => rig.Client.Checkouts.CreateAsync(User, null!));
        yield return Bad("ledger: limit 0", rig => rig.Client.Ledger.ListAsync(User, new LedgerQuery { Limit = 0 }));
        yield return Bad("ledger: negative limit", rig => rig.Client.Ledger.ListAsync(User, new LedgerQuery { Limit = -3 }));
        yield return Bad("ledger: limit 101", rig => rig.Client.Ledger.ListAsync(User, new LedgerQuery { Limit = 101 }));
        yield return Bad("ledger: empty currency", rig => rig.Client.Ledger.ListAsync(User, new LedgerQuery { Currency = "" }));
        yield return Bad("ledger: empty cursor", rig => rig.Client.Ledger.ListAsync(User, new LedgerQuery { Cursor = "" }));
        yield return Bad("checkout: empty success url", rig => rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter", SuccessUrl = "" }));
        yield return Bad("checkout: empty cancel url", rig => rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter", CancelUrl = "" }));
        yield return Bad("checkout: empty country", rig => rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter", Country = "" }));
        yield return Bad("checkout: empty locale", rig => rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter", Locale = "" }));
        yield return Bad("checkout: empty creator code", rig => rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter", CreatorCode = "" }));
        yield return Bad("codes.redeem: default player", rig => rig.Client.Codes.RedeemAsync(none, "WELCOME"));
        yield return Bad("codes.redeem: empty code", rig => rig.Client.Codes.RedeemAsync(User, ""));
        yield return Bad("codes.redeem: null code", rig => rig.Client.Codes.RedeemAsync(User, null!));
        yield return Bad("upsert: empty display name", rig => rig.Client.Players.UpsertAsync(User, new PlayerProfile { DisplayName = "" }));
        yield return Bad("upsert: empty email", rig => rig.Client.Players.UpsertAsync(User, new PlayerProfile { Email = "" }));
        yield return Bad("upsert: birth year 0", rig => rig.Client.Players.UpsertAsync(User, new PlayerProfile { BirthYear = 0 }));
        yield return Bad("orders.get: empty id", rig => rig.Client.Orders.GetAsync(""));
        yield return Bad("orders.get: null id", rig => rig.Client.Orders.GetAsync(null!));
        yield return Bad("orders.refund: empty id", rig => rig.Client.Orders.RefundAsync(""));
    }

    private static object[] Bad(string name, Func<TestRig, Task> call) => new object[] { name, call };

    [Theory]
    [MemberData(nameof(BadCalls))]
    public async Task Invalid_arguments_raise_an_argument_error_before_any_request(string name, Func<TestRig, Task> call)
    {
        var rig = new TestRig();

        var error = await Assert.ThrowsAnyAsync<ArgumentException>(() => call(rig));

        Assert.Empty(rig.Handler.Requests);
        Assert.False(string.IsNullOrWhiteSpace(error.Message), name);
    }

    [Fact]
    public async Task Messages_name_the_argument_never_its_value()
    {
        var rig = new TestRig();

        var amount = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = -123456 }));
        var limit = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            rig.Client.Ledger.ListAsync(User, new LedgerQuery { Limit = 987654 }));
        var key = await Assert.ThrowsAsync<ArgumentException>(() =>
            rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 }, new RequestOptions { IdempotencyKey = "tab	here-SECRETISH" }));

        Assert.DoesNotContain("123456", amount.Message);
        Assert.Null(amount.ActualValue);
        Assert.DoesNotContain("987654", limit.Message);
        Assert.Null(limit.ActualValue);
        Assert.DoesNotContain("SECRETISH", key.Message);
        Assert.Contains("Amount", amount.Message);
        Assert.Contains("1 to 100", limit.Message);
    }

    [Fact]
    public void Iterating_checks_its_arguments_right_away_and_sends_nothing()
    {
        var rig = new TestRig();

        Assert.Throws<ArgumentOutOfRangeException>(() => rig.Client.Ledger.IterateAsync(User, new LedgerQuery { Limit = 0 }));
        Assert.Throws<ArgumentException>(() => rig.Client.Ledger.IterateAsync(default));

        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public async Task Negative_amounts_raise_ArgumentOutOfRange()
    {
        var rig = new TestRig();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = -5 }));
    }

    // --- the secret key ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_key_is_absent_from_every_kind_of_error()
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler
            .Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", "Invalid API key")
            .Error(HttpStatusCode.Conflict, "INSUFFICIENT_FUNDS", "Insufficient funds", """{"required":1,"available":0}""")
            .Throw(new HttpRequestException("Connection refused (gc.example.test:443)"))
            .Raw(HttpStatusCode.BadGateway, "<html>gateway</html>")
            .Ok("not json");
        var fast = new TestRig(maxRetries: 0, timeout: TimeSpan.FromMilliseconds(50));
        fast.Handler.Hang();

        var errors = new List<GameCoinException>
        {
            await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync()),
            await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Wallet.SpendAsync(User, new SpendRequest { Currency = "gems", Amount = 1 })),
            await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync()),
            await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync()),
            await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync()),
            await Assert.ThrowsAsync<GameCoinException>(() => fast.Client.Catalog.GetAsync()),
        };

        Assert.Equal(6, errors.Count);
        foreach (var error in errors)
        {
            AssertNoSecret(error.Message);
            AssertNoSecret(error.ToString());
            AssertNoSecret(error.Code);
            AssertNoSecret(string.Join("|", error.Details.Select(d => d.Key + d.Value)));
        }
    }

    [Fact]
    public async Task A_server_that_echoes_the_key_in_its_message_does_not_leak_it()
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Error(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", $"Unknown key {TestRig.SecretKey}");

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync());

        AssertNoSecret(error.Message);
        Assert.Contains("[redacted]", error.Message);
    }

    [Fact]
    public async Task A_network_stack_that_echoes_the_key_does_not_leak_it()
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Throw(new HttpRequestException($"Failed with header Authorization: Bearer {TestRig.SecretKey}"));

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync());

        AssertNoSecret(error.Message);
        AssertNoSecret(error.ToString());
    }

    [Fact]
    public async Task The_key_is_absent_from_the_string_form_of_every_result()
    {
        var rig = new TestRig();
        rig.Handler
            .Ok(Samples.Catalog).Ok(Samples.Player).Ok("""{"token":"gc_pt_x","expiresAt":"2026-10-05T21:38:32.000Z"}""")
            .Ok("""{"playerId":"p","balances":[]}""").Ok(Samples.Movement).Ok("""{"entries":[],"nextCursor":null}""")
            .Ok($$"""{"item":{{Samples.Item}}}""").Ok($$"""{"order":{{Samples.Order}},"checkoutUrl":"u"}""").Ok(Samples.Redemption).Ok(Samples.Order);

        var results = new List<object>
        {
            await rig.Client.Catalog.GetAsync(),
            await rig.Client.Players.GetAsync(User),
            await rig.Client.Players.CreateTokenAsync(User),
            await rig.Client.Wallet.GetAsync(User),
            await rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 }),
            await rig.Client.Ledger.ListAsync(User),
            await rig.Client.Inventory.GrantAsync(User, new InventoryGrantRequest { Sku = "potion" }),
            await rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter" }),
            await rig.Client.Codes.RedeemAsync(User, "WELCOME"),
            await rig.Client.Orders.RefundAsync("o1"),
            rig.Client,
        };

        foreach (var result in results)
        {
            AssertNoSecret(result.ToString()!);
        }
    }

    [Fact]
    public void The_key_is_not_reachable_through_the_public_surface()
    {
        // No public member of the client or of its options exposes the key.
        var publicMembers = typeof(GameCoinClient).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(m => m.Name);
        Assert.DoesNotContain(publicMembers, name => name.Contains("Secret", StringComparison.OrdinalIgnoreCase) || name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase));
        var optionMembers = typeof(GameCoinOptions).GetMembers(BindingFlags.Public | BindingFlags.Instance).Select(m => m.Name);
        Assert.DoesNotContain(optionMembers, name => name.Contains("Key", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertNoSecret(string text)
    {
        Assert.DoesNotContain(TestRig.SecretKey, text);
        Assert.DoesNotContain("SECRETVALUE", text);
    }
}
