using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Apilow.GameCoin.Tests;

/// <summary>0.4: catalog management (currencies, items, packs): requests, results, arguments, retries.</summary>
public class CatalogManagementTests
{
    private const string CurrencyJson =
        """{"code":"gems","name":"Gems","purchasable":true,"status":"active","createdAt":"2026-10-05T12:00:00.000Z","updatedAt":"2026-10-05T13:00:00.000Z","somethingNew":1}""";

    private const string ItemJson =
        """{"sku":"potion","name":"Potion","description":null,"type":"consumable","price":{"currency":"gems","amount":20},"maxOwned":null,"clientPurchasable":true,"metadata":{"heal":"50"},"status":"active","createdAt":"2026-10-05T12:00:00.000Z","updatedAt":"2026-10-05T13:00:00.000Z"}""";

    private const string PackJson =
        """{"sku":"gems-500","name":"500 gems","description":null,"priceCents":499,"currency":"EUR","grants":[{"currency":"gems","amount":500,"bonus":50}],"items":[{"sku":"potion","quantity":2}],"badge":"popular","maxPerPlayer":null,"sortOrder":10,"status":"active","availability":{"startsAt":"2026-11-01T00:00:00.000Z","endsAt":null,"stock":1000,"founder":false,"sold":4},"createdAt":"2026-10-05T12:00:00.000Z","updatedAt":"2026-10-05T13:00:00.000Z"}""";

    private static void AssertJson(string expected, string actual)
    {
        using var e = JsonDocument.Parse(expected);
        using var a = JsonDocument.Parse(actual);
        Assert.Equal(Canonical(e.RootElement), Canonical(a.RootElement));
    }

    private static string Canonical(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => $"\"{p.Name}\":{Canonical(p.Value)}")) + "}",
        JsonValueKind.Array => "[" + string.Join(",", element.EnumerateArray().Select(Canonical)) + "]",
        _ => element.GetRawText(),
    };

    private static async Task<RecordedRequest> Call(string reply, Func<GameCoinClient, Task> call)
    {
        var rig = new TestRig();
        rig.Handler.Ok(reply);
        await call(rig.Client);
        var request = rig.Only;
        Assert.False(request.HasHeader("Idempotency-Key"), "a catalog call carries no idempotency key");
        Assert.StartsWith("Bearer gc_sk_test_", request.Header("Authorization"));
        return request;
    }

    // --- requests --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Currencies_requests()
    {
        var list = await Call("""{"currencies":[]}""", c => c.Currencies.ListAsync());
        Assert.Equal((HttpMethod.Get, "/api/v1/currencies"), (list.Method, list.PathAndQuery));

        var archived = await Call("""{"currencies":[]}""", c => c.Currencies.ListAsync(includeArchived: true));
        Assert.Equal("/api/v1/currencies?includeArchived=true", archived.PathAndQuery);

        var create = await Call(CurrencyJson, c => c.Currencies.CreateAsync(new CreateCurrencyRequest { Code = "gems", Name = "Gems", Purchasable = true }));
        Assert.Equal((HttpMethod.Post, "/api/v1/currencies"), (create.Method, create.PathAndQuery));
        AssertJson("""{"code":"gems","name":"Gems","purchasable":true}""", create.Body);

        var minimal = await Call(CurrencyJson, c => c.Currencies.CreateAsync(new CreateCurrencyRequest { Code = "gems", Name = "Gems" }));
        AssertJson("""{"code":"gems","name":"Gems"}""", minimal.Body);

        var get = await Call(CurrencyJson, c => c.Currencies.GetAsync("gems"));
        Assert.Equal((HttpMethod.Get, "/api/v1/currencies/gems", ""), (get.Method, get.PathAndQuery, get.Body));

        var update = await Call(CurrencyJson, c => c.Currencies.UpdateAsync("gems", new UpdateCurrencyRequest { Name = "Shiny", Purchasable = false }));
        Assert.Equal((HttpMethod.Patch, "/api/v1/currencies/gems"), (update.Method, update.PathAndQuery));
        AssertJson("""{"name":"Shiny","purchasable":false}""", update.Body);

        var nothing = await Call(CurrencyJson, c => c.Currencies.UpdateAsync("gems", null));
        AssertJson("{}", nothing.Body);

        var archive = await Call(CurrencyJson, c => c.Currencies.ArchiveAsync("gems"));
        Assert.Equal((HttpMethod.Delete, "/api/v1/currencies/gems", ""), (archive.Method, archive.PathAndQuery, archive.Body));
    }

    [Fact]
    public async Task Items_requests()
    {
        var get = await Call(ItemJson, c => c.Items.GetAsync("a b/c"));
        Assert.Equal("/api/v1/items/a%20b%2Fc", get.PathAndQuery);

        var create = await Call(ItemJson, c => c.Items.CreateAsync(new CreateItemRequest
        {
            Sku = "potion",
            Name = "Potion",
            Type = ItemType.Consumable,
            Price = new ItemPrice { Currency = "gems", Amount = 20 },
            ClientPurchasable = true,
            Metadata = new Dictionary<string, string> { ["heal"] = "50" },
        }));
        Assert.Equal((HttpMethod.Post, "/api/v1/items"), (create.Method, create.PathAndQuery));
        AssertJson("""{"sku":"potion","name":"Potion","type":"consumable","price":{"currency":"gems","amount":20},"clientPurchasable":true,"metadata":{"heal":"50"}}""", create.Body);

        var defaults = await Call(ItemJson, c => c.Items.CreateAsync(new CreateItemRequest { Sku = "x", Name = "X", Type = ItemType.Durable }));
        AssertJson("""{"sku":"x","name":"X","type":"durable"}""", defaults.Body);

        var unlimited = await Call(ItemJson, c => c.Items.CreateAsync(new CreateItemRequest { Sku = "x", Name = "X", Type = ItemType.Durable, NoMaxOwned = true }));
        AssertJson("""{"sku":"x","name":"X","type":"durable","maxOwned":null}""", unlimited.Body);

        var update = await Call(ItemJson, c => c.Items.UpdateAsync("potion", new UpdateItemRequest
        {
            Name = "Big potion",
            Price = new ItemPrice { Currency = "gems", Amount = 25 },
            MaxOwned = 5,
        }));
        Assert.Equal(HttpMethod.Patch, update.Method);
        AssertJson("""{"name":"Big potion","price":{"currency":"gems","amount":25},"maxOwned":5}""", update.Body);

        var clear = await Call(ItemJson, c => c.Items.UpdateAsync("potion", new UpdateItemRequest { Clear = ItemFields.Price | ItemFields.Description | ItemFields.MaxOwned }));
        AssertJson("""{"description":null,"price":null,"maxOwned":null}""", clear.Body);

        var metadata = await Call(ItemJson, c => c.Items.UpdateAsync("potion", new UpdateItemRequest { Metadata = new Dictionary<string, string>() }));
        AssertJson("""{"metadata":{}}""", metadata.Body);

        var list = await Call("""{"items":[]}""", c => c.Items.ListAsync());
        Assert.Equal("/api/v1/items", list.PathAndQuery);
        var archive = await Call(ItemJson, c => c.Items.ArchiveAsync("potion"));
        Assert.Equal((HttpMethod.Delete, "/api/v1/items/potion"), (archive.Method, archive.PathAndQuery));
    }

    [Fact]
    public async Task Packs_requests()
    {
        var create = await Call(PackJson, c => c.Packs.CreateAsync(new CreatePackRequest
        {
            Sku = "gems-500",
            Name = "500 gems",
            PriceCents = 499,
            Grants = new[] { new PackGrant { Currency = "gems", Amount = 500, Bonus = 50 } },
            Items = new[] { new PackItem { Sku = "potion", Quantity = 2 } },
            Badge = "popular",
            MaxPerPlayer = 1,
            SortOrder = 10,
            Availability = new PackAvailabilityRequest
            {
                StartsAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero),
                EndsAt = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.FromHours(1)),
                Stock = 1000,
                Founder = true,
            },
            Status = PackStatus.Active,
        }));
        Assert.Equal((HttpMethod.Post, "/api/v1/packs"), (create.Method, create.PathAndQuery));
        AssertJson(
            """{"sku":"gems-500","name":"500 gems","priceCents":499,"grants":[{"currency":"gems","amount":500,"bonus":50}],"items":[{"sku":"potion","quantity":2}],"badge":"popular","maxPerPlayer":1,"sortOrder":10,"availability":{"startsAt":"2026-11-01T00:00:00Z","endsAt":"2026-11-30T23:00:00Z","stock":1000,"founder":true},"status":"active"}""",
            create.Body);

        var minimal = await Call(PackJson, c => c.Packs.CreateAsync(new CreatePackRequest { Sku = "x", Name = "X", PriceCents = 500, Items = new[] { new PackItem { Sku = "y", Quantity = 1 } } }));
        AssertJson("""{"sku":"x","name":"X","priceCents":500,"items":[{"sku":"y","quantity":1}]}""", minimal.Body);

        var update = await Call(PackJson, c => c.Packs.UpdateAsync("gems-500", new UpdatePackRequest
        {
            PriceCents = 599,
            SortOrder = -3,
            Availability = new PackAvailabilityRequest(),
            Status = PackStatus.Active,
            Clear = PackFields.Badge,
        }));
        Assert.Equal((HttpMethod.Patch, "/api/v1/packs/gems-500"), (update.Method, update.PathAndQuery));
        AssertJson(
            """{"priceCents":599,"sortOrder":-3,"availability":{"startsAt":null,"endsAt":null,"stock":null,"founder":false},"status":"active","badge":null}""",
            update.Body);

        var emptyLists = await Call(PackJson, c => c.Packs.UpdateAsync("gems-500", new UpdatePackRequest { Grants = Array.Empty<PackGrant>(), Items = Array.Empty<PackItem>() }));
        AssertJson("""{"grants":[],"items":[]}""", emptyLists.Body);

        var list = await Call("""{"packs":[]}""", c => c.Packs.ListAsync(includeArchived: true));
        Assert.Equal("/api/v1/packs?includeArchived=true", list.PathAndQuery);
        var get = await Call(PackJson, c => c.Packs.GetAsync("gems-500"));
        Assert.Equal("/api/v1/packs/gems-500", get.PathAndQuery);
        var archive = await Call(PackJson, c => c.Packs.ArchiveAsync("gems-500"));
        Assert.Equal(HttpMethod.Delete, archive.Method);
    }

    // --- results ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Results_are_typed_and_unknown_fields_are_ignored()
    {
        var rig = new TestRig();
        rig.Handler.Ok(CurrencyJson).Ok(ItemJson).Ok(PackJson).Ok($$"""{"packs":[{{PackJson}},{{PackJson}}]}""");

        var currency = await rig.Client.Currencies.GetAsync("gems");
        Assert.Equal(("gems", "Gems", true, CatalogStatus.Active), (currency.Code, currency.Name, currency.Purchasable, currency.Status));
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero), currency.CreatedAt);
        Assert.Equal(TimeSpan.FromHours(1), currency.UpdatedAt - currency.CreatedAt);

        var item = await rig.Client.Items.GetAsync("potion");
        Assert.Equal(20, item.Price!.Amount);
        Assert.Null(item.MaxOwned);
        Assert.Equal("50", item.Metadata["heal"]);
        Assert.Equal(CatalogStatus.Active, item.Status);

        var pack = await rig.Client.Packs.GetAsync("gems-500");
        Assert.Equal((10L, PackStatus.Active, "popular"), (pack.SortOrder, pack.Status, pack.Badge));
        Assert.Equal(("gems", 500L, 50L), (pack.Grants[0].Currency, pack.Grants[0].Amount, pack.Grants[0].Bonus));
        Assert.Equal("potion", pack.Items[0].Sku);
        var availability = pack.Availability!;
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), availability.StartsAt);
        Assert.Null(availability.EndsAt);
        Assert.Equal((1000L, false, 4L), (availability.Stock, availability.Founder, availability.Sold));

        var packs = await rig.Client.Packs.ListAsync();
        Assert.Equal(2, packs.Count);
    }

    [Fact]
    public async Task A_pack_without_availability_reads_as_null()
    {
        var rig = new TestRig();
        rig.Handler.Ok(PackJson.Replace("\"availability\":{\"startsAt\":\"2026-11-01T00:00:00.000Z\",\"endsAt\":null,\"stock\":1000,\"founder\":false,\"sold\":4}", "\"availability\":null"));
        Assert.Null((await rig.Client.Packs.GetAsync("gems-500")).Availability);
    }

    [Fact]
    public async Task A_response_of_the_wrong_shape_is_INVALID_RESPONSE()
    {
        var rig = new TestRig();
        rig.Handler.Ok("""{"code":"gems"}""");
        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Currencies.GetAsync("gems"));
        Assert.Equal(GameCoinErrorCodes.InvalidResponse, error.Code);
    }

    [Fact]
    public async Task Errors_carry_their_details()
    {
        var rig = new TestRig();
        rig.Handler.Error(HttpStatusCode.Conflict, "CONFLICT", "in use", """{"blockedBy":{"packs":["gems-500"],"items":[]}}""");
        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Currencies.ArchiveAsync("gems"));
        Assert.Equal(GameCoinErrorCodes.Conflict, error.Code);
        Assert.Equal("gems-500", error.Details["blockedBy"].GetProperty("packs")[0].GetString());
    }

    // --- arguments -------------------------------------------------------------------------------------------------------

    public static TheoryData<string, Func<GameCoinClient, Task>> BadCalls => new()
    {
        { "empty code", c => c.Currencies.GetAsync("") },
        { "no code", c => c.Currencies.CreateAsync(new CreateCurrencyRequest { Name = "Gems" }) },
        { "no name", c => c.Currencies.CreateAsync(new CreateCurrencyRequest { Code = "gems" }) },
        { "empty name on update", c => c.Currencies.UpdateAsync("gems", new UpdateCurrencyRequest { Name = "" }) },
        { "no item type", c => c.Items.CreateAsync(new CreateItemRequest { Sku = "x", Name = "X" }) },
        { "unknown item type", c => c.Items.CreateAsync(new CreateItemRequest { Sku = "x", Name = "X", Type = "weapon" }) },
        { "price of 0", c => c.Items.CreateAsync(new CreateItemRequest { Sku = "x", Name = "X", Type = ItemType.Durable, Price = new ItemPrice { Currency = "gems" } }) },
        { "max owned of 0", c => c.Items.CreateAsync(new CreateItemRequest { Sku = "x", Name = "X", Type = ItemType.Durable, MaxOwned = 0 }) },
        { "max owned and no max owned", c => c.Items.CreateAsync(new CreateItemRequest { Sku = "x", Name = "X", Type = ItemType.Durable, MaxOwned = 2, NoMaxOwned = true }) },
        { "item field set and cleared", c => c.Items.UpdateAsync("x", new UpdateItemRequest { Price = new ItemPrice { Currency = "gems", Amount = 1 }, Clear = ItemFields.Price }) },
        { "negative price", c => c.Packs.CreateAsync(new CreatePackRequest { Sku = "x", Name = "X", PriceCents = -1 }) },
        { "grant without a currency", c => c.Packs.CreateAsync(new CreatePackRequest { Sku = "x", Name = "X", PriceCents = 500, Grants = new[] { new PackGrant { Amount = 5 } } }) },
        { "item quantity of 0", c => c.Packs.CreateAsync(new CreatePackRequest { Sku = "x", Name = "X", PriceCents = 500, Items = new[] { new PackItem { Sku = "y" } } }) },
        { "badge out of the list", c => c.Packs.CreateAsync(new CreatePackRequest { Sku = "x", Name = "X", PriceCents = 500, Badge = "hot" }) },
        { "archived on create", c => c.Packs.CreateAsync(new CreatePackRequest { Sku = "x", Name = "X", PriceCents = 500, Status = PackStatus.Archived }) },
        { "stock of 0", c => c.Packs.UpdateAsync("x", new UpdatePackRequest { Availability = new PackAvailabilityRequest { Stock = 0 } }) },
        { "status out of the list", c => c.Packs.UpdateAsync("x", new UpdatePackRequest { Status = "deleted" }) },
        { "max per player of 0", c => c.Packs.UpdateAsync("x", new UpdatePackRequest { MaxPerPlayer = 0 }) },
        { "pack field set and cleared", c => c.Packs.UpdateAsync("x", new UpdatePackRequest { Badge = "popular", Clear = PackFields.Badge }) },
    };

    [Theory]
    [MemberData(nameof(BadCalls))]
    public async Task Bad_arguments_never_reach_the_network(string name, Func<GameCoinClient, Task> call)
    {
        _ = name;
        var rig = new TestRig();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => call(rig.Client));
        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public async Task A_null_create_request_is_refused()
    {
        var rig = new TestRig();
        await Assert.ThrowsAsync<ArgumentNullException>(() => rig.Client.Packs.CreateAsync(null!));
        Assert.Empty(rig.Handler.Requests);
    }

    // --- retries ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_create_is_retried_on_429_only()
    {
        var rig = new TestRig();
        rig.Handler.Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "slow down", null, ("Retry-After", "2")).Ok(CurrencyJson);
        await rig.Client.Currencies.CreateAsync(new CreateCurrencyRequest { Code = "gems", Name = "Gems" });
        Assert.Equal(2, rig.Handler.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, rig.Delays);

        var creates = new Func<GameCoinClient, Task>[]
        {
            c => c.Currencies.CreateAsync(new CreateCurrencyRequest { Code = "gems", Name = "Gems" }),
            c => c.Items.CreateAsync(new CreateItemRequest { Sku = "x", Name = "X", Type = ItemType.Durable }),
            c => c.Packs.CreateAsync(new CreatePackRequest { Sku = "x", Name = "X", PriceCents = 500, Items = new[] { new PackItem { Sku = "y", Quantity = 1 } } }),
        };
        foreach (var create in creates)
        {
            var failing = new TestRig();
            failing.Handler.Error(HttpStatusCode.BadGateway, "INTERNAL", "bad gateway");
            await Assert.ThrowsAsync<GameCoinException>(() => create(failing.Client));
            Assert.Single(failing.Handler.Requests);

            var dropped = new TestRig();
            dropped.Handler.Throw(new HttpRequestException("boom"));
            var error = await Assert.ThrowsAsync<GameCoinException>(() => create(dropped.Client));
            Assert.Equal(GameCoinErrorCodes.NetworkError, error.Code);
            Assert.Single(dropped.Handler.Requests);
        }
    }

    [Fact]
    public async Task Reads_updates_and_archives_are_retried_on_5xx()
    {
        var calls = new Func<GameCoinClient, Task>[]
        {
            c => c.Currencies.UpdateAsync("gems", new UpdateCurrencyRequest { Name = "G" }),
            c => c.Items.ArchiveAsync("potion"),
            c => c.Packs.GetAsync("x"),
            c => c.Packs.ListAsync(),
        };
        foreach (var call in calls)
        {
            var rig = new TestRig();
            rig.Handler.Error(HttpStatusCode.ServiceUnavailable, "INTERNAL", "unavailable").Ok("""{"unrelated":true}""");
            try
            {
                await call(rig.Client);
            }
            catch (GameCoinException)
            {
                // the second reply is not the expected shape: only the retry matters here
            }

            Assert.Equal(2, rig.Handler.Requests.Count);
        }
    }

    [Fact]
    public void The_version_is_0_4_0()
    {
        Assert.Equal("0.4.0", GameCoinClient.SdkVersion);
    }
}
