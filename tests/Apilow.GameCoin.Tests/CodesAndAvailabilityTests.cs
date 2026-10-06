using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Apilow.GameCoin.Tests;

/// <summary>0.3: gift codes, creator code on checkouts, pack availability, prelaunch, the new error codes.</summary>
public class CodesAndAvailabilityTests
{
    private static readonly PlayerRef User = PlayerRef.Ext("user-42");

    private const string CheckoutOk = $$"""{"order":{{Samples.Order}},"checkoutUrl":"u"}""";

    // --- codes.redeem: request ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Codes_redeem_posts_the_code_with_an_idempotency_key()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Redemption);

        await rig.Client.Codes.RedeemAsync(User, "WELCOME-2026");

        Assert.Equal(HttpMethod.Post, rig.Only.Method);
        Assert.Equal("/api/v1/players/ext%3Auser-42/codes/redeem", rig.Only.PathAndQuery);
        Assert.Equal("""{"code":"WELCOME-2026"}""", rig.Only.Body);
        Assert.True(rig.Only.HasHeader("Idempotency-Key"));
        Assert.StartsWith("application/json", rig.Only.Header("Content-Type"));
    }

    [Theory]
    [InlineData("  wel come-2026 ")]
    [InlineData("welcome")]
    [InlineData("a\"b\\c")]
    [InlineData("été-✨")]
    public async Task Codes_redeem_sends_the_code_untouched(string code)
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Redemption);

        await rig.Client.Codes.RedeemAsync(User, code); // the server ignores case, spaces and dashes: the SDK does not normalise

        Assert.Equal(code, rig.Only.Json().GetProperty("code").GetString());
    }

    [Fact]
    public async Task Codes_redeem_encodes_a_raw_player_id()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Redemption);

        await rig.Client.Codes.RedeemAsync(PlayerRef.FromId("6ac40ac0"), "C");

        Assert.Equal("/api/v1/players/6ac40ac0/codes/redeem", rig.Only.PathAndQuery);
    }

    // --- codes.redeem: result -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Codes_redeem_parses_grants_items_and_skipped()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Redemption);

        var redemption = await rig.Client.Codes.RedeemAsync(User, "WELCOME");

        Assert.Equal(2, redemption.Grants.Count);
        Assert.Equal("gems", redemption.Grants[0].Currency);
        Assert.Equal(50, redemption.Grants[0].Amount);
        Assert.Equal(new Balance { Currency = "gems", Paid = 0, Bonus = 150, Total = 150 }, redemption.Grants[0].Balance);
        Assert.Equal("gold", redemption.Grants[1].Currency);
        Assert.Equal(10, redemption.Grants[1].Balance.Total);
        Assert.Equal(new RedemptionItem { Sku = "potion", Quantity = 2 }, Assert.Single(redemption.Items));
        Assert.Equal(new RedemptionItem { Sku = "fire-sword", Quantity = 1 }, Assert.Single(redemption.Skipped));
        Assert.False(redemption.Replayed);
    }

    [Fact]
    public async Task Codes_redeem_accepts_empty_lists()
    {
        var rig = new TestRig();
        rig.Handler.Ok("""{"grants":[],"items":[],"skipped":[]}""");

        var redemption = await rig.Client.Codes.RedeemAsync(User, "ITEMSONLY");

        Assert.Empty(redemption.Grants);
        Assert.Empty(redemption.Items);
        Assert.Empty(redemption.Skipped);
    }

    [Fact]
    public async Task Codes_redeem_exposes_the_replay_flag_and_keeps_the_caller_key()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Redemption).Ok(Samples.Redemption, ("Idempotent-Replayed", "true"));
        var options = new RequestOptions { IdempotencyKey = "redeem-user-42-welcome" };

        var first = await rig.Client.Codes.RedeemAsync(User, "WELCOME", options);
        var second = await rig.Client.Codes.RedeemAsync(User, "WELCOME", options);

        Assert.False(first.Replayed);
        Assert.True(second.Replayed);
        Assert.Equal(first.Grants, second.Grants); // never credited twice: same body
        Assert.All(rig.Handler.Requests, r => Assert.Equal("redeem-user-42-welcome", r.Header("Idempotency-Key")));
    }

    // --- codes.redeem: errors -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_refused_gift_code_is_VALIDATION_FAILED_with_CODE_INVALID_and_is_never_retried()
    {
        var rig = new TestRig();
        rig.Handler.Error(HttpStatusCode.BadRequest, "VALIDATION_FAILED", "Invalid request", """{"fieldErrors":{"code":["CODE_INVALID"]}}""");

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Codes.RedeemAsync(User, "NOPE"));

        Assert.Equal(GameCoinErrorCodes.ValidationFailed, error.Code);
        Assert.Equal(400, error.Status);
        Assert.Equal("CODE_INVALID", error.Details["fieldErrors"].GetProperty("code")[0].GetString());
        Assert.Single(rig.Handler.Requests);
        Assert.Empty(rig.Delays);
    }

    [Theory]
    [InlineData(403, "PLAYER_BLOCKED")]
    [InlineData(409, "LIMIT_REACHED")]
    [InlineData(409, "CONFLICT")]
    public async Task Other_redeem_refusals_come_through_the_usual_mapping_and_are_not_retried(int status, string code)
    {
        var rig = new TestRig();
        rig.Handler.Error((HttpStatusCode)status, code, "Refused");

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Codes.RedeemAsync(User, "C"));

        Assert.Equal(code, error.Code);
        Assert.Equal(status, error.Status);
        Assert.Single(rig.Handler.Requests);
    }

    [Fact]
    public async Task A_rate_limited_redeem_carries_retry_after()
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "Too many attempts", null, ("Retry-After", "600"));

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Codes.RedeemAsync(User, "C"));

        Assert.Equal(GameCoinErrorCodes.RateLimited, error.Code);
        Assert.Equal(TimeSpan.FromSeconds(600), error.RetryAfter);
    }

    [Fact]
    public async Task A_redeem_is_retried_on_5xx_with_the_same_key_so_it_cannot_credit_twice()
    {
        var rig = new TestRig();
        rig.Handler.Error(HttpStatusCode.ServiceUnavailable, "INTERNAL", "down").Ok(Samples.Redemption);

        var redemption = await rig.Client.Codes.RedeemAsync(User, "C");

        Assert.Equal(2, rig.Handler.Requests.Count);
        Assert.Equal(rig.Handler.Requests[0].Header("Idempotency-Key"), rig.Handler.Requests[1].Header("Idempotency-Key"));
        Assert.Equal(2, redemption.Grants.Count);
    }

    // --- creatorCode on checkouts ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Checkouts_create_sends_the_creator_code_when_given()
    {
        var rig = new TestRig();
        rig.Handler.Ok(CheckoutOk);

        await rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter", CreatorCode = "streamer-10" });

        Assert.Equal("""{"packSku":"starter","creatorCode":"streamer-10"}""", rig.Only.Body); // sent untouched
    }

    [Fact]
    public async Task Checkouts_create_does_not_send_the_creator_code_when_absent()
    {
        var rig = new TestRig();
        rig.Handler.Ok(CheckoutOk);

        await rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter", CreatorCode = null });

        Assert.DoesNotContain("creatorCode", rig.Only.Body);
    }

    [Fact]
    public async Task Checkouts_create_with_every_field_including_the_creator_code()
    {
        var rig = new TestRig();
        rig.Handler.Ok(CheckoutOk);

        await rig.Client.Checkouts.CreateAsync(
            User,
            new CheckoutRequest
            {
                PackSku = "gems-500",
                SuccessUrl = "https://game.test/ok",
                CancelUrl = "https://game.test/ko",
                Country = "BE",
                Locale = CheckoutLocale.French,
                CreatorCode = "STREAMER10",
            });

        Assert.Equal(
            """{"packSku":"gems-500","successUrl":"https://game.test/ok","cancelUrl":"https://game.test/ko","country":"BE","locale":"fr","creatorCode":"STREAMER10"}""",
            rig.Only.Body);
    }

    [Fact]
    public async Task A_refused_creator_code_is_VALIDATION_FAILED_with_CODE_INVALID()
    {
        var rig = new TestRig();
        rig.Handler.Error(HttpStatusCode.BadRequest, "VALIDATION_FAILED", "Invalid request", """{"fieldErrors":{"creatorCode":["CODE_INVALID"]}}""");

        var error = await Assert.ThrowsAsync<GameCoinException>(() =>
            rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "starter", CreatorCode = "UNKNOWN" }));

        Assert.Equal(GameCoinErrorCodes.ValidationFailed, error.Code);
        Assert.Equal(400, error.Status);
        Assert.Equal("CODE_INVALID", error.Details["fieldErrors"].GetProperty("creatorCode")[0].GetString());
        Assert.Single(rig.Handler.Requests); // a 400 is never retried
    }

    [Fact]
    public async Task The_order_of_a_checkout_shows_the_creator_code_and_the_creator_name_only()
    {
        var rig = new TestRig();
        rig.Handler.Reply(HttpStatusCode.Created, $$"""{"order":{{Samples.OrderWithCreator}},"checkoutUrl":"u"}""");

        var checkout = await rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "gems-500", CreatorCode = "streamer 10" });

        Assert.Equal(new OrderCreatorCode { Code = "STREAMER10", CreatorName = "Ann the Streamer" }, checkout.Order.CreatorCode);
    }

    // --- Order.CreatorCode ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_order_without_a_creator_code_has_null()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Order);

        var order = await rig.Client.Orders.GetAsync("o1");

        Assert.Null(order.CreatorCode);
    }

    [Fact]
    public async Task An_order_from_an_older_api_without_the_creator_code_key_reads_as_null()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Order.Replace(",\"creatorCode\":null", string.Empty));

        var order = await rig.Client.Orders.GetAsync("o1");

        Assert.Null(order.CreatorCode);
    }

    [Fact]
    public async Task An_order_with_a_creator_code_ignores_unknown_fields_of_it()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.OrderWithCreator.Replace("\"creatorName\"", "\"rate\":10,\"creatorName\""));

        var order = await rig.Client.Orders.GetAsync("o2");

        Assert.Equal("STREAMER10", order.CreatorCode!.Code);
        Assert.Equal("Ann the Streamer", order.CreatorCode.CreatorName);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
    }

    // --- Catalog: availability and prelaunch ----------------------------------------------------------------------------

    private static string CatalogWithPacks(string packs, string tail = "\"prelaunch\":false,\"launchAt\":null") =>
        $$"""{"currencies":[],"items":[],"packs":[{{packs}}],{{tail}}}""";

    private static string Pack(string sku, string availability) =>
        $$"""{"sku":"{{sku}}","name":"{{sku}}","description":null,"priceCents":499,"currency":"EUR","grants":[],"items":[],"badge":null,"maxPerPlayer":null,"availability":{{availability}}}""";

    [Fact]
    public async Task An_ordinary_pack_has_no_availability_and_the_catalog_is_not_prelaunch()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Catalog);

        var catalog = await rig.Client.Catalog.GetAsync();

        Assert.Null(Assert.Single(catalog.Packs).Availability);
        Assert.False(catalog.Prelaunch);
        Assert.Null(catalog.LaunchAt);
    }

    [Fact]
    public async Task A_founder_pack_with_a_window_and_a_stock()
    {
        var rig = new TestRig();
        rig.Handler.Ok(CatalogWithPacks(Pack("founder", """{"startsAt":"2026-12-01T09:00:00.000Z","endsAt":"2026-12-31T23:59:59.000+01:00","remaining":42,"founder":true}""")));

        var availability = (await rig.Client.Catalog.GetAsync()).Packs[0].Availability;

        Assert.NotNull(availability);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero), availability.StartsAt);
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 22, 59, 59, TimeSpan.Zero), availability.EndsAt); // offsets are honoured
        Assert.Equal(42, availability.Remaining);
        Assert.True(availability.Founder);
    }

    [Fact]
    public async Task An_open_ended_pack_with_an_unlimited_stock_has_nulls()
    {
        var rig = new TestRig();
        rig.Handler.Ok(CatalogWithPacks(Pack("seasonal", """{"startsAt":"2026-11-01T00:00:00.000Z","endsAt":null,"remaining":null,"founder":false}""")));

        var availability = (await rig.Client.Catalog.GetAsync()).Packs[0].Availability!;

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), availability.StartsAt);
        Assert.Null(availability.EndsAt);
        Assert.Null(availability.Remaining);
        Assert.False(availability.Founder);
    }

    [Fact]
    public async Task A_sold_out_pack_stays_in_the_catalog_with_remaining_zero()
    {
        var rig = new TestRig();
        rig.Handler.Ok(CatalogWithPacks(
            Pack("sold-out", """{"startsAt":null,"endsAt":null,"remaining":0,"founder":true}""") + "," + Pack("plain", "null")));

        var catalog = await rig.Client.Catalog.GetAsync();

        Assert.Equal(2, catalog.Packs.Count);
        Assert.Equal(0, catalog.Packs[0].Availability!.Remaining);
        Assert.Null(catalog.Packs[0].Availability!.StartsAt);
        Assert.Null(catalog.Packs[1].Availability);
    }

    [Fact]
    public async Task Unknown_fields_of_the_availability_are_ignored()
    {
        var rig = new TestRig();
        rig.Handler.Ok(CatalogWithPacks(Pack("p", """{"startsAt":null,"endsAt":null,"remaining":3,"founder":false,"sold":7,"stock":10}""")));

        var availability = (await rig.Client.Catalog.GetAsync()).Packs[0].Availability!;

        Assert.Equal(3, availability.Remaining);
    }

    [Fact]
    public async Task A_prelaunch_catalog_has_a_launch_date()
    {
        var rig = new TestRig();
        rig.Handler.Ok(CatalogWithPacks(string.Empty, "\"prelaunch\":true,\"launchAt\":\"2027-03-15T10:00:00.000Z\""));

        var catalog = await rig.Client.Catalog.GetAsync();

        Assert.True(catalog.Prelaunch);
        Assert.Equal(new DateTimeOffset(2027, 3, 15, 10, 0, 0, TimeSpan.Zero), catalog.LaunchAt);
        Assert.Empty(catalog.Packs);
    }

    [Fact]
    public async Task A_catalog_from_an_older_api_without_the_new_keys_reads_as_not_prelaunch_and_ordinary_packs()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Catalog
            .Replace(",\"prelaunch\":false,\"launchAt\":null", string.Empty)
            .Replace(",\"availability\":null", string.Empty));

        var catalog = await rig.Client.Catalog.GetAsync();

        Assert.False(catalog.Prelaunch);
        Assert.Null(catalog.LaunchAt);
        Assert.Null(catalog.Packs[0].Availability);
    }

    // --- PACK_NOT_AVAILABLE and PACK_SOLD_OUT ---------------------------------------------------------------------------

    [Fact]
    public void The_new_error_code_constants_have_the_api_values()
    {
        Assert.Equal("PACK_NOT_AVAILABLE", GameCoinErrorCodes.PackNotAvailable);
        Assert.Equal("PACK_SOLD_OUT", GameCoinErrorCodes.PackSoldOut);
    }

    [Fact]
    public async Task A_pack_outside_its_window_is_PACK_NOT_AVAILABLE_with_the_window_in_the_details()
    {
        var rig = new TestRig();
        rig.Handler.Error(
            HttpStatusCode.Conflict,
            "PACK_NOT_AVAILABLE",
            "This pack is not on sale",
            """{"startsAt":"2026-12-01T09:00:00.000Z","endsAt":null}""");

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "founder" }));

        Assert.Equal(GameCoinErrorCodes.PackNotAvailable, error.Code);
        Assert.Equal(409, error.Status);
        Assert.Equal(JsonValueKind.String, error.Details["startsAt"].ValueKind);
        Assert.Equal("2026-12-01T09:00:00.000Z", error.Details["startsAt"].GetString());
        Assert.Equal(JsonValueKind.Null, error.Details["endsAt"].ValueKind);
        Assert.True(error.Details["startsAt"].TryGetDateTimeOffset(out var startsAt));
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero), startsAt);
        Assert.Single(rig.Handler.Requests); // a 409 is never retried
        Assert.Empty(rig.Delays);
    }

    [Fact]
    public async Task A_window_that_has_ended_has_an_end_and_possibly_no_start()
    {
        var rig = new TestRig();
        rig.Handler.Error(HttpStatusCode.Conflict, "PACK_NOT_AVAILABLE", "Over", """{"startsAt":null,"endsAt":"2026-10-01T00:00:00.000Z"}""");

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "founder" }));

        Assert.Equal(JsonValueKind.Null, error.Details["startsAt"].ValueKind);
        Assert.Equal("2026-10-01T00:00:00.000Z", error.Details["endsAt"].GetString());
    }

    [Fact]
    public async Task A_pack_without_a_unit_left_is_PACK_SOLD_OUT_with_the_pack_sku()
    {
        var rig = new TestRig();
        rig.Handler.Error(HttpStatusCode.Conflict, "PACK_SOLD_OUT", "Sold out", """{"packSku":"founder"}""");

        var caught = false;
        try
        {
            await rig.Client.Checkouts.CreateAsync(User, new CheckoutRequest { PackSku = "founder" });
        }
        catch (GameCoinException ex) when (ex.Code == GameCoinErrorCodes.PackSoldOut)
        {
            caught = true;
            Assert.Equal(409, ex.Status);
            Assert.Equal("founder", ex.Details["packSku"].GetString());
        }

        Assert.True(caught);
        Assert.Single(rig.Handler.Requests);
    }

    // --- Webhook events carry the creator code --------------------------------------------------------------------------

    [Theory]
    [InlineData("order.fulfilled")]
    [InlineData("order.refunded")]
    [InlineData("order.chargeback")]
    public void An_order_event_carries_the_creator_code_when_the_order_has_one(string type)
    {
        var body = Event(type, Samples.OrderWithCreator);

        var order = Assert.IsType<Order>(Verify(body).Data);

        Assert.Equal(new OrderCreatorCode { Code = "STREAMER10", CreatorName = "Ann the Streamer" }, order.CreatorCode);
    }

    [Fact]
    public void An_order_event_with_a_null_creator_code()
    {
        var order = Assert.IsType<Order>(Verify(Event("order.fulfilled", Samples.Order)).Data);

        Assert.Null(order.CreatorCode);
    }

    [Fact]
    public void An_order_event_sent_before_creator_codes_existed_is_still_accepted()
    {
        var body = Event("order.fulfilled", Samples.Order.Replace(",\"creatorCode\":null", string.Empty));

        var order = Assert.IsType<Order>(Verify(body).Data);

        Assert.Null(order.CreatorCode);
        Assert.Equal("gems-500", order.Pack.Sku);
    }

    [Fact]
    public void An_order_event_with_a_malformed_creator_code_is_an_invalid_payload()
    {
        var body = Event("order.fulfilled", Samples.Order.Replace("\"creatorCode\":null", "\"creatorCode\":{\"code\":\"X\"}"));

        var error = Assert.Throws<WebhookSignatureException>(() => Verify(body));

        Assert.Equal(WebhookSignatureReason.InvalidPayload, error.Reason);
        Assert.Contains("creatorCode", error.Message);
    }

    private const string Secret = "whsec_gc_SECRETVALUE0123456789";
    private const long Now = 1_790_000_000;

    private static string Event(string type, string order) =>
        $$"""{"id":"evt_x_{{type}}","type":"{{type}}","createdAt":"2026-10-06T10:00:02.000Z","env":"test","data":{{order}}}""";

    private static WebhookEvent Verify(string body)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(Secret));
        var signature = Convert.ToHexString(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{Now}.{body}"))).ToLowerInvariant();
        return Webhooks.ConstructEvent(body, $"t={Now},v1={signature}", Secret, new WebhookOptions { Now = () => DateTimeOffset.FromUnixTimeSeconds(Now) });
    }
}
