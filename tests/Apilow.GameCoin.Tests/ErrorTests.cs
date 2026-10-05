using System.Net;
using System.Text.Json;

namespace Apilow.GameCoin.Tests;

public class ErrorTests
{
    private static readonly PlayerRef User = PlayerRef.Ext("user-42");

    [Fact]
    public async Task Insufficient_funds_exposes_required_and_available()
    {
        var rig = new TestRig();
        rig.Handler.Error(HttpStatusCode.Conflict, "INSUFFICIENT_FUNDS", "Insufficient funds", """{"required":50,"available":20}""");

        var error = await Assert.ThrowsAsync<GameCoinException>(() =>
            rig.Client.Wallet.SpendAsync(User, new SpendRequest { Currency = "gems", Amount = 50 }));

        Assert.Equal(GameCoinErrorCodes.InsufficientFunds, error.Code);
        Assert.Equal("INSUFFICIENT_FUNDS", error.Code);
        Assert.Equal(409, error.Status);
        Assert.Equal("Insufficient funds", error.Message);
        Assert.Equal(50, error.Details["required"].GetInt64());
        Assert.Equal(20, error.Details["available"].GetInt64());
        Assert.Null(error.RetryAfter);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task Validation_failures_expose_field_errors()
    {
        var rig = new TestRig();
        rig.Handler.Error(
            HttpStatusCode.BadRequest,
            "VALIDATION_FAILED",
            "Invalid request",
            """{"fieldErrors":{"amount":["Too small"],"_":["Unrecognized field"]}}""");

        var error = await Assert.ThrowsAsync<GameCoinException>(() =>
            rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 }));

        Assert.Equal(GameCoinErrorCodes.ValidationFailed, error.Code);
        Assert.Equal(400, error.Status);
        var fieldErrors = error.Details["fieldErrors"];
        Assert.Equal(JsonValueKind.Object, fieldErrors.ValueKind);
        Assert.Equal("Too small", fieldErrors.GetProperty("amount")[0].GetString());
    }

    [Fact]
    public async Task Details_are_empty_when_the_api_sends_none()
    {
        var rig = new TestRig();
        rig.Handler.Error(HttpStatusCode.NotFound, "NOT_FOUND", "Player not found");

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Players.GetAsync(User));

        Assert.Equal(GameCoinErrorCodes.NotFound, error.Code);
        Assert.Equal(404, error.Status);
        Assert.NotNull(error.Details);
        Assert.Empty(error.Details);
    }

    [Fact]
    public async Task Details_stay_readable_after_the_response_is_gone()
    {
        var rig = new TestRig();
        rig.Handler.Error(HttpStatusCode.Conflict, "INSUFFICIENT_FUNDS", "x", """{"required":5,"available":1,"nested":{"a":[1,2,3]}}""");

        var error = await Assert.ThrowsAsync<GameCoinException>(() =>
            rig.Client.Wallet.SpendAsync(User, new SpendRequest { Currency = "gems", Amount = 5 }));
        GC.Collect();

        Assert.Equal(3, error.Details["nested"].GetProperty("a").GetArrayLength());
    }

    [Fact]
    public async Task Rate_limited_carries_retry_after()
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "Too many requests", null, ("Retry-After", "12"));

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync());

        Assert.Equal(GameCoinErrorCodes.RateLimited, error.Code);
        Assert.Equal(429, error.Status);
        Assert.Equal(TimeSpan.FromSeconds(12), error.RetryAfter);
    }

    [Fact]
    public async Task Retry_after_is_kept_when_the_retries_are_exhausted()
    {
        var rig = new TestRig(maxRetries: 1);
        rig.Handler
            .Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "x", null, ("Retry-After", "1"))
            .Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "x", null, ("Retry-After", "2"));

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync());

        Assert.Equal(TimeSpan.FromSeconds(2), error.RetryAfter);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("soon")]
    [InlineData("")]
    public async Task A_malformed_retry_after_is_ignored(string value)
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "x", null, ("Retry-After", value));

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync());

        Assert.Null(error.RetryAfter);
    }

    [Fact]
    public async Task A_network_failure_has_status_zero_and_keeps_the_cause()
    {
        var rig = new TestRig(maxRetries: 0);
        var cause = new HttpRequestException("No connection could be made");
        rig.Handler.Throw(cause);

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync());

        Assert.Equal(GameCoinErrorCodes.NetworkError, error.Code);
        Assert.Equal("NETWORK_ERROR", error.Code);
        Assert.Equal(0, error.Status);
        Assert.Same(cause, error.InnerException);
        Assert.Contains("No connection could be made", error.Message);
        Assert.Empty(error.Details);
    }

    [Fact]
    public async Task An_io_failure_while_reading_is_a_network_error_too()
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Throw(new IOException("The response ended prematurely"));

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync());

        Assert.Equal(GameCoinErrorCodes.NetworkError, error.Code);
    }

    [Fact]
    public async Task A_timeout_has_status_zero()
    {
        var rig = new TestRig(maxRetries: 0, timeout: TimeSpan.FromMilliseconds(50));
        rig.Handler.Hang();

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync());

        Assert.Equal("TIMEOUT", error.Code);
        Assert.Equal(0, error.Status);
        Assert.Contains("No response within", error.Message);
    }

    [Theory]
    [InlineData("<html>Welcome to a captive portal</html>")]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    [InlineData("""{"entry":"this should be an object","balance":1}""")]
    public async Task A_success_body_that_is_not_the_expected_json_is_INVALID_RESPONSE(string body)
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Ok(body);

        var error = await Assert.ThrowsAsync<GameCoinException>(() =>
            rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 }));

        Assert.Equal(GameCoinErrorCodes.InvalidResponse, error.Code);
        Assert.Equal("INVALID_RESPONSE", error.Code);
        Assert.Equal(200, error.Status);
        Assert.Single(rig.Handler.Requests); // a 2xx is never retried
    }

    [Theory]
    [InlineData("<html>oops</html>")]
    [InlineData("""{"message":"not our format"}""")]
    [InlineData("""{"error":"a string"}""")]
    [InlineData("""{"error":{"message":"no code"}}""")]
    [InlineData("")]
    public async Task An_error_status_with_an_unexpected_body_is_INVALID_RESPONSE_with_the_http_status(string body)
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Raw(HttpStatusCode.BadRequest, body, "application/json");

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync());

        Assert.Equal(GameCoinErrorCodes.InvalidResponse, error.Code);
        Assert.Equal(400, error.Status);
    }

    [Fact]
    public async Task An_error_without_a_message_is_not_a_gamecoin_error_body()
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Raw(HttpStatusCode.Conflict, """{"error":{"code":"CONFLICT"}}""", "application/json");

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync());

        Assert.Equal(GameCoinErrorCodes.InvalidResponse, error.Code);
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public async Task An_unknown_future_error_code_is_passed_through()
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Error(HttpStatusCode.Conflict, "SOMETHING_NEW", "A new rule");

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync());

        Assert.Equal("SOMETHING_NEW", error.Code);
        Assert.Equal("A new rule", error.Message);
    }

    [Fact]
    public async Task A_redirect_is_reported_with_a_hint_not_followed()
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Raw(HttpStatusCode.MovedPermanently, string.Empty, "text/plain", ("Location", "https://elsewhere.test/"));

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync());

        Assert.Equal(GameCoinErrorCodes.InvalidResponse, error.Code);
        Assert.Equal(301, error.Status);
        Assert.Contains("base URL", error.Message);
    }

    [Fact]
    public async Task Internal_errors_are_retried_and_raised()
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Error(HttpStatusCode.InternalServerError, "INTERNAL", "Internal error");

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Catalog.GetAsync());

        Assert.Equal(GameCoinErrorCodes.Internal, error.Code);
        Assert.Equal(500, error.Status);
    }

    [Fact]
    public void The_exception_can_be_built_by_hand()
    {
        var error = new GameCoinException("NOT_FOUND", 404, "Player not found");

        Assert.Equal("NOT_FOUND", error.Code);
        Assert.Equal(404, error.Status);
        Assert.Empty(error.Details);
        Assert.Null(error.RetryAfter);
        Assert.Contains("NOT_FOUND", error.ToString());
        Assert.Contains("404", error.ToString());
    }

    [Fact]
    public async Task Error_handling_with_a_filter_reads_naturally()
    {
        var rig = new TestRig();
        rig.Handler.Error(HttpStatusCode.Conflict, "INSUFFICIENT_FUNDS", "Insufficient funds", """{"required":50,"available":20}""");

        var caught = false;
        try
        {
            await rig.Client.Wallet.SpendAsync(User, new SpendRequest { Currency = "gems", Amount = 50 });
        }
        catch (GameCoinException ex) when (ex.Code == GameCoinErrorCodes.InsufficientFunds)
        {
            caught = true;
            Assert.Equal(30, ex.Details["required"].GetInt64() - ex.Details["available"].GetInt64());
        }

        Assert.True(caught);
    }
}
