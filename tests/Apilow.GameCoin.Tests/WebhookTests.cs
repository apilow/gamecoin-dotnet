using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Apilow.GameCoin.Tests;

public class WebhookTests
{
    // A made-up signing secret: the real ones (whsec_gc_...) are shown once in the dashboard.
    private const string Secret = "whsec_gc_SECRETVALUE0123456789";
    private const long Now = 1_790_000_000;

    private static readonly DateTimeOffset Clock = DateTimeOffset.FromUnixTimeSeconds(Now);

    // The order.fulfilled example of the webhooks guide.
    private const string OrderFulfilledBody = """
        {
          "id": "evt_6ac419fbdbe02af6c99c3d47_ful",
          "type": "order.fulfilled",
          "createdAt": "2026-10-06T10:00:02.000Z",
          "env": "test",
          "data": {
            "id": "6ac419fbdbe02af6c99c3d47",
            "playerId": "6ac419fa60e877541898f38d",
            "status": "fulfilled",
            "pack": { "sku": "gems-500", "name": "Bag of gems" },
            "amountCents": 499,
            "currency": "EUR",
            "country": "BE",
            "vatRateBp": 2100,
            "vatCents": 87,
            "netCents": 412,
            "createdAt": "2026-10-06T10:00:00.000Z",
            "paidAt": "2026-10-06T10:00:01.000Z",
            "fulfilledAt": "2026-10-06T10:00:01.000Z",
            "refundedAt": null
          }
        }
        """;

    private const string WalletCreditedBody = """
        {"id":"evt_6ac419fbdbe02af6c99c3d47_cre","type":"wallet.credited","createdAt":"2026-10-06T10:00:02.000Z","env":"test","data":{"playerId":"6ac419fa60e877541898f38d","orderId":"6ac419fbdbe02af6c99c3d47","credits":[{"currency":"gems","paid":500,"bonus":50}]}}
        """;

    private const string TestBody = """
        {"id":"evt_test_1","type":"webhook.test","createdAt":"2026-10-06T10:00:02.000Z","env":"live","data":{"message":"This is a test event sent from the GameCoin dashboard."}}
        """;

    private static string Hmac(string secret, long timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Header(string body, long timestamp = Now, string secret = Secret) =>
        $"t={timestamp},v1={Hmac(secret, timestamp, body)}";

    private static WebhookOptions AtNow(int tolerance = 300) => new() { ToleranceSeconds = tolerance, Now = () => Clock };

    private static WebhookEvent Verify(string body, string? header, string secret = Secret, WebhookOptions? options = null) =>
        Webhooks.ConstructEvent(body, header, secret, options ?? AtNow());

    private static WebhookSignatureException Refused(string body, string? header, string secret = Secret, WebhookOptions? options = null) =>
        Assert.Throws<WebhookSignatureException>(() => Verify(body, header, secret, options));

    // --- The happy path -----------------------------------------------------------------------------------------------

    [Fact]
    public void Verifies_the_example_of_the_guide_and_types_the_order()
    {
        var webhookEvent = Verify(OrderFulfilledBody, Header(OrderFulfilledBody));

        Assert.Equal("evt_6ac419fbdbe02af6c99c3d47_ful", webhookEvent.Id);
        Assert.Equal(WebhookEventTypes.OrderFulfilled, webhookEvent.Type);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 10, 0, 2, TimeSpan.Zero), webhookEvent.CreatedAt);
        Assert.Equal("test", webhookEvent.Env);
        var order = Assert.IsType<Order>(webhookEvent.Data);
        Assert.Equal("6ac419fbdbe02af6c99c3d47", order.Id);
        Assert.Equal(OrderStatus.Fulfilled, order.Status);
        Assert.Equal("gems-500", order.Pack.Sku);
        Assert.Equal(499, order.AmountCents);
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 10, 0, 1, TimeSpan.Zero), order.PaidAt);
        Assert.Null(order.RefundedAt);
    }

    [Fact]
    public void Text_and_bytes_bodies_give_the_same_event()
    {
        var header = Header(OrderFulfilledBody);

        var fromText = Webhooks.ConstructEvent(OrderFulfilledBody, header, Secret, AtNow());
        var fromBytes = Webhooks.ConstructEvent(Encoding.UTF8.GetBytes(OrderFulfilledBody), header, Secret, AtNow());

        Assert.Equal(fromText, fromBytes);
    }

    [Fact]
    public void A_body_with_non_ascii_characters_is_signed_as_utf8()
    {
        var body = OrderFulfilledBody.Replace("Bag of gems", "Sac de gemmes été ✨");

        var order = Assert.IsType<Order>(Verify(body, Header(body)).Data);

        Assert.Equal("Sac de gemmes été ✨", order.Pack.Name);
    }

    [Fact]
    public void Works_without_options_with_the_system_clock()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var webhookEvent = Webhooks.ConstructEvent(TestBody, Header(TestBody, timestamp), Secret);

        Assert.Equal(WebhookEventTypes.WebhookTest, webhookEvent.Type);
    }

    [Fact]
    public void The_secret_is_trimmed()
    {
        Verify(OrderFulfilledBody, Header(OrderFulfilledBody), secret: "  " + Secret + "\n");
    }

    [Fact]
    public void Hexadecimal_in_capitals_is_accepted()
    {
        var header = $"t={Now},v1={Hmac(Secret, Now, TestBody).ToUpperInvariant()}";

        Verify(TestBody, header);
    }

    [Fact]
    public void White_space_around_the_parts_of_the_header_is_tolerated()
    {
        var header = $" t = {Now} , v1 = {Hmac(Secret, Now, TestBody)} ";

        Verify(TestBody, header);
    }

    [Fact]
    public void Unknown_keys_and_parts_without_a_value_are_ignored()
    {
        var header = $"v0=abc,t={Now},extra,scheme=future,v1={Hmac(Secret, Now, TestBody)}";

        Verify(TestBody, header);
    }

    // --- Several signatures --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Accepts_the_call_when_one_of_several_v1_is_right(bool rightFirst)
    {
        var right = Hmac(Secret, Now, TestBody);
        var wrong = Hmac("whsec_gc_OLDSECRET", Now, TestBody);
        var header = rightFirst ? $"t={Now},v1={right},v1={wrong}" : $"t={Now},v1={wrong},v1={right}";

        Verify(TestBody, header);
    }

    [Fact]
    public void Refuses_the_call_when_every_v1_is_wrong()
    {
        var header = $"t={Now},v1={Hmac("whsec_gc_OLD1", Now, TestBody)},v1={Hmac("whsec_gc_OLD2", Now, TestBody)}";

        Assert.Equal(WebhookSignatureReason.NoMatchingSignature, Refused(TestBody, header).Reason);
    }

    [Theory]
    [InlineData("zz")]                                    // not hexadecimal
    [InlineData("abcd")]                                  // too short
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")] // right size, wrong value
    [InlineData("not hexadecimal but exactly as long as a sha-256 digest in hex......")] // right size, not hexadecimal
    public void A_v1_that_is_not_a_signature_never_matches(string given)
    {
        Assert.Equal(WebhookSignatureReason.NoMatchingSignature, Refused(TestBody, $"t={Now},v1={given}").Reason);
    }

    // --- Refusals -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Refuses_a_wrong_secret()
    {
        var error = Refused(OrderFulfilledBody, Header(OrderFulfilledBody), secret: "whsec_gc_ANOTHERSECRET");

        Assert.Equal(WebhookSignatureReason.NoMatchingSignature, error.Reason);
    }

    [Fact]
    public void Refuses_a_body_altered_by_one_character()
    {
        var header = Header(OrderFulfilledBody);
        var altered = OrderFulfilledBody.Replace("\"amountCents\": 499", "\"amountCents\": 498");

        Assert.Equal(WebhookSignatureReason.NoMatchingSignature, Refused(altered, header).Reason);
    }

    [Fact]
    public void Refuses_a_body_that_was_parsed_and_serialized_again()
    {
        var header = Header(OrderFulfilledBody);
        var reserialized = JsonSerializer.Serialize(JsonDocument.Parse(OrderFulfilledBody).RootElement);

        Assert.Equal(WebhookSignatureReason.NoMatchingSignature, Refused(reserialized, header).Reason);
    }

    [Fact]
    public void Refuses_a_signature_computed_for_another_timestamp()
    {
        var header = $"t={Now + 1},v1={Hmac(Secret, Now, TestBody)}";

        Assert.Equal(WebhookSignatureReason.NoMatchingSignature, Refused(TestBody, header).Reason);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(3600)]
    public void Refuses_an_expired_timestamp(int secondsAgo)
    {
        var error = Refused(TestBody, Header(TestBody, Now - secondsAgo));

        Assert.Equal(WebhookSignatureReason.TimestampOutOfTolerance, error.Reason);
    }

    [Fact]
    public void Refuses_a_timestamp_in_the_future()
    {
        var error = Refused(TestBody, Header(TestBody, Now + 301));

        Assert.Equal(WebhookSignatureReason.TimestampOutOfTolerance, error.Reason);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(-300)]
    [InlineData(0)]
    public void Accepts_a_timestamp_within_the_tolerance_edge_included(int offset)
    {
        Verify(TestBody, Header(TestBody, Now + offset));
    }

    [Fact]
    public void Tolerance_zero_turns_the_timestamp_check_off()
    {
        Verify(TestBody, Header(TestBody, 1_000_000), options: AtNow(tolerance: 0));
        Verify(TestBody, Header(TestBody, Now + 1_000_000), options: AtNow(tolerance: 0));
    }

    [Fact]
    public void A_custom_tolerance_applies()
    {
        Verify(TestBody, Header(TestBody, Now - 60), options: AtNow(tolerance: 60));
        Assert.Equal(WebhookSignatureReason.TimestampOutOfTolerance, Refused(TestBody, Header(TestBody, Now - 61), options: AtNow(tolerance: 60)).Reason);
        Verify(TestBody, Header(TestBody, Now - 3000), options: AtNow(tolerance: 3600));
    }

    [Fact]
    public void The_injected_clock_is_read_for_every_call()
    {
        var clock = Clock;
        var options = new WebhookOptions { Now = () => clock };
        var header = Header(TestBody);

        Verify(TestBody, header, options: options);
        clock = Clock.AddMinutes(10);
        Assert.Equal(WebhookSignatureReason.TimestampOutOfTolerance, Refused(TestBody, header, options: options).Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_a_missing_header(string? header)
    {
        Assert.Equal(WebhookSignatureReason.MissingHeader, Refused(TestBody, header).Reason);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("t=1790000000")]                                    // no signature
    [InlineData("v1=abcdef")]                                       // no timestamp
    [InlineData("t=abc,v1=abcdef")]                                 // timestamp not a number
    [InlineData("t=,v1=abcdef")]                                    // empty timestamp
    [InlineData("t=-5,v1=abcdef")]                                  // negative timestamp
    [InlineData("t=1790000000.5,v1=abcdef")]                        // fractional timestamp
    [InlineData("t=99999999999999999999,v1=abcdef")]                // does not fit in a long
    [InlineData("t=1790000000,t=1790000001,v1=abcdef")]             // two timestamps
    [InlineData("t=1790000000,v1=")]                                // empty signature
    [InlineData(",,,")]
    public void Refuses_a_malformed_header(string header)
    {
        Assert.Equal(WebhookSignatureReason.MalformedHeader, Refused(TestBody, header).Reason);
    }

    [Fact]
    public void The_signature_is_checked_before_the_body_is_parsed()
    {
        var notJson = "this is not json";

        // A wrong signature never reveals anything about the body...
        Assert.Equal(WebhookSignatureReason.NoMatchingSignature, Refused(notJson, $"t={Now},v1={Hmac("whsec_gc_OTHER", Now, notJson)}").Reason);
        // ...a right one on a body that is not an event says so.
        Assert.Equal(WebhookSignatureReason.InvalidPayload, Refused(notJson, Header(notJson)).Reason);
    }

    // --- Events -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Types_wallet_credited()
    {
        var webhookEvent = Verify(WalletCreditedBody, Header(WalletCreditedBody));

        Assert.Equal(WebhookEventTypes.WalletCredited, webhookEvent.Type);
        var data = Assert.IsType<WalletCreditedData>(webhookEvent.Data);
        Assert.Equal("6ac419fa60e877541898f38d", data.PlayerId);
        Assert.Equal("6ac419fbdbe02af6c99c3d47", data.OrderId);
        var credit = Assert.Single(data.Credits);
        Assert.Equal(new WalletCredit { Currency = "gems", Paid = 500, Bonus = 50 }, credit);
    }

    [Fact]
    public void Types_webhook_test()
    {
        var webhookEvent = Verify(TestBody, Header(TestBody));

        Assert.Equal(WebhookEventTypes.WebhookTest, webhookEvent.Type);
        Assert.Equal("live", webhookEvent.Env);
        Assert.Equal("This is a test event sent from the GameCoin dashboard.", Assert.IsType<WebhookTestData>(webhookEvent.Data).Message);
    }

    [Theory]
    [InlineData("order.fulfilled", "fulfilled")]
    [InlineData("order.refunded", "refunded")]
    [InlineData("order.chargeback", "chargeback")]
    public void Types_every_order_event(string type, string status)
    {
        var body = OrderFulfilledBody.Replace("\"type\": \"order.fulfilled\"", $"\"type\": \"{type}\"").Replace("\"status\": \"fulfilled\"", $"\"status\": \"{status}\"");

        var webhookEvent = Verify(body, Header(body));

        Assert.Equal(type, webhookEvent.Type);
        Assert.Equal(status, Assert.IsType<Order>(webhookEvent.Data).Status);
    }

    [Fact]
    public void The_event_type_constants_name_every_event()
    {
        Assert.Equal(
            new[] { "order.fulfilled", "order.refunded", "order.chargeback", "wallet.credited", "webhook.test" },
            new[] { WebhookEventTypes.OrderFulfilled, WebhookEventTypes.OrderRefunded, WebhookEventTypes.OrderChargeback, WebhookEventTypes.WalletCredited, WebhookEventTypes.WebhookTest });
    }

    [Fact]
    public void The_reason_constants_name_every_failure()
    {
        Assert.Equal(
            new[] { "missing_header", "malformed_header", "no_matching_signature", "timestamp_out_of_tolerance", "invalid_payload" },
            new[]
            {
                WebhookSignatureReason.MissingHeader, WebhookSignatureReason.MalformedHeader, WebhookSignatureReason.NoMatchingSignature,
                WebhookSignatureReason.TimestampOutOfTolerance, WebhookSignatureReason.InvalidPayload,
            });
    }

    [Fact]
    public void An_unknown_type_is_passed_through_with_untyped_data()
    {
        const string body = """{"id":"evt_x","type":"payout.created","createdAt":"2027-01-01T00:00:00Z","env":"live","data":{"amount":12,"nested":{"a":[1,2]}}}""";

        var webhookEvent = Verify(body, Header(body));

        Assert.Equal("payout.created", webhookEvent.Type);
        var data = Assert.IsType<JsonElement>(webhookEvent.Data);
        Assert.Equal(12, data.GetProperty("amount").GetInt32());
        Assert.Equal(2, data.GetProperty("nested").GetProperty("a")[1].GetInt32());
    }

    [Fact]
    public void Unknown_fields_of_a_known_event_are_ignored()
    {
        var body = WalletCreditedBody.Replace("\"env\":\"test\"", "\"env\":\"test\",\"somethingNew\":{\"a\":1}").Replace("\"bonus\":50", "\"bonus\":50,\"extra\":true");

        Verify(body, Header(body));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{\"id\":")]                                                          // truncated
    [InlineData("[1,2,3]")]                                                            // not an object
    [InlineData("\"text\"")]
    [InlineData("""{"type":"webhook.test","createdAt":"2026-10-06T10:00:02Z","env":"test","data":{"message":"m"}}""")]                  // no id
    [InlineData("""{"id":"e","createdAt":"2026-10-06T10:00:02Z","env":"test","data":{"message":"m"}}""")]                               // no type
    [InlineData("""{"id":"e","type":"webhook.test","env":"test","data":{"message":"m"}}""")]                                            // no createdAt
    [InlineData("""{"id":"e","type":"webhook.test","createdAt":"yesterday","env":"test","data":{"message":"m"}}""")]                    // bad date
    [InlineData("""{"id":"e","type":"webhook.test","createdAt":"2026-10-06T10:00:02Z","data":{"message":"m"}}""")]                      // no env
    [InlineData("""{"id":"e","type":"webhook.test","createdAt":"2026-10-06T10:00:02Z","env":"test"}""")]                                // no data
    [InlineData("""{"id":7,"type":"webhook.test","createdAt":"2026-10-06T10:00:02Z","env":"test","data":{"message":"m"}}""")]           // id not a string
    [InlineData("""{"id":"e","type":"webhook.test","createdAt":"2026-10-06T10:00:02Z","env":"test","data":{}}""")]                      // test without message
    [InlineData("""{"id":"e","type":"wallet.credited","createdAt":"2026-10-06T10:00:02Z","env":"test","data":{"playerId":"p"}}""")]    // wallet data incomplete
    [InlineData("""{"id":"e","type":"wallet.credited","createdAt":"2026-10-06T10:00:02Z","env":"test","data":{"playerId":"p","orderId":"o","credits":[{"currency":"gems","paid":"5","bonus":0}]}}""")] // text amount
    [InlineData("""{"id":"e","type":"order.refunded","createdAt":"2026-10-06T10:00:02Z","env":"test","data":{"id":"o"}}""")]            // order incomplete
    [InlineData("""{"id":"e","type":"order.fulfilled","createdAt":"2026-10-06T10:00:02Z","env":"test","data":null}""")]                 // order is null
    public void Refuses_a_body_that_is_not_an_event(string body)
    {
        var error = Refused(body, Header(body));

        Assert.Equal(WebhookSignatureReason.InvalidPayload, error.Reason);
    }

    [Fact]
    public void A_body_that_is_not_utf8_is_not_an_event()
    {
        var body = new byte[] { 0x7B, 0xFF, 0xFE, 0x7D }; // { 0xFF 0xFE }
        var header = $"t={Now},v1={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.ASCII.GetBytes($"{Now}.").Concat(body).ToArray())).ToLowerInvariant()}";

        var error = Assert.Throws<WebhookSignatureException>(() => Webhooks.ConstructEvent(body, header, Secret, AtNow()));

        Assert.Equal(WebhookSignatureReason.InvalidPayload, error.Reason);
    }

    // --- Arguments and secrecy ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_secret_is_an_argument_error(string secret)
    {
        var error = Assert.Throws<ArgumentException>(() => Webhooks.ConstructEvent(TestBody, Header(TestBody), secret, AtNow()));

        Assert.Equal("secret", error.ParamName);
    }

    [Fact]
    public void A_null_body_is_an_argument_error_that_asks_for_the_raw_body()
    {
        var text = Assert.Throws<ArgumentNullException>(() => Webhooks.ConstructEvent((string)null!, Header(TestBody), Secret));
        var bytes = Assert.Throws<ArgumentNullException>(() => Webhooks.ConstructEvent((byte[])null!, Header(TestBody), Secret));

        Assert.Contains("raw body", text.Message);
        Assert.Contains("raw body", bytes.Message);
    }

    [Fact]
    public void A_negative_tolerance_is_an_argument_error()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Webhooks.ConstructEvent(TestBody, Header(TestBody), Secret, new WebhookOptions { ToleranceSeconds = -1 }));
    }

    [Fact]
    public void The_signature_error_is_not_an_api_error()
    {
        var error = Refused(TestBody, null);

        Assert.IsNotType<GameCoinException>(error);
        Assert.Equal(typeof(Exception), typeof(WebhookSignatureException).BaseType);
    }

    [Fact]
    public void Errors_never_contain_the_secret_the_body_or_the_signature()
    {
        var goodSignature = Hmac(Secret, Now, OrderFulfilledBody);
        var cases = new List<Func<WebhookSignatureException>>
        {
            () => Refused(OrderFulfilledBody, null),
            () => Refused(OrderFulfilledBody, "garbage"),
            () => Refused(OrderFulfilledBody, $"t={Now},v1={goodSignature}", secret: "whsec_gc_WRONGSECRET0123"),
            () => Refused(OrderFulfilledBody + " ", $"t={Now},v1={goodSignature}"),
            () => Refused(OrderFulfilledBody, Header(OrderFulfilledBody, Now - 10_000)),
            () => Refused("not json SECRETBODY", Header("not json SECRETBODY")),
        };

        foreach (var make in cases)
        {
            var error = make();
            foreach (var text in new[] { error.Message, error.ToString() })
            {
                Assert.DoesNotContain(Secret, text);
                Assert.DoesNotContain("SECRETVALUE", text);
                Assert.DoesNotContain("WRONGSECRET", text);
                Assert.DoesNotContain(goodSignature, text);
                Assert.DoesNotContain("SECRETBODY", text);
                Assert.DoesNotContain("evt_6ac419fbdbe02af6c99c3d47", text);
            }
        }
    }

    [Fact]
    public void Argument_errors_never_contain_the_secret_either()
    {
        var error = Assert.Throws<ArgumentException>(() => Webhooks.ConstructEvent(TestBody, Header(TestBody), "   "));

        Assert.DoesNotContain("SECRETVALUE", error.Message);
    }
}
