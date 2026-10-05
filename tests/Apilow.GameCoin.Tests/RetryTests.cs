using System.Net;

namespace Apilow.GameCoin.Tests;

public class RetryTests
{
    private static readonly PlayerRef User = PlayerRef.Ext("user-42");

    private static Task<Movement> Grant(TestRig rig) =>
        rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 });

    [Fact]
    public async Task Retries_after_a_network_error()
    {
        var rig = new TestRig();
        rig.Handler.Throw(new HttpRequestException("Connection refused")).Ok(Samples.Movement);

        var movement = await Grant(rig);

        Assert.Equal(100, movement.Balance.Total);
        Assert.Equal(2, rig.Handler.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(0.5) }, rig.Delays);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task Retries_on_5xx(int status)
    {
        var rig = new TestRig();
        rig.Handler.Error((HttpStatusCode)status, "INTERNAL", "oops").Ok(Samples.Movement);

        await Grant(rig);

        Assert.Equal(2, rig.Handler.Requests.Count);
    }

    [Fact]
    public async Task Backoff_doubles_from_half_a_second()
    {
        var rig = new TestRig(maxRetries: 4);
        rig.Handler
            .Error(HttpStatusCode.InternalServerError, "INTERNAL", "1")
            .Error(HttpStatusCode.InternalServerError, "INTERNAL", "2")
            .Error(HttpStatusCode.InternalServerError, "INTERNAL", "3")
            .Error(HttpStatusCode.InternalServerError, "INTERNAL", "4")
            .Ok(Samples.Movement);

        await Grant(rig);

        Assert.Equal(5, rig.Handler.Requests.Count);
        Assert.Equal(
            new[] { TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) },
            rig.Delays);
    }

    [Theory]
    [InlineData(0.0, 0.375)] // 0.5 s x 0.75
    [InlineData(1.0, 0.625)] // 0.5 s x 1.25
    public async Task Backoff_is_jittered_by_a_quarter(double jitter, double expectedSeconds)
    {
        var rig = new TestRig(jitter: jitter);
        rig.Handler.Throw(new HttpRequestException("x")).Ok(Samples.Movement);

        await Grant(rig);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), Assert.Single(rig.Delays));
    }

    [Fact]
    public async Task Gives_up_after_max_retries_and_raises_the_last_error()
    {
        var rig = new TestRig(); // MaxRetries = 2 -> three attempts
        rig.Handler
            .Error(HttpStatusCode.ServiceUnavailable, "INTERNAL", "first")
            .Error(HttpStatusCode.ServiceUnavailable, "INTERNAL", "second")
            .Error(HttpStatusCode.ServiceUnavailable, "INTERNAL", "third")
            .Ok(Samples.Movement);

        var error = await Assert.ThrowsAsync<GameCoinException>(() => Grant(rig));

        Assert.Equal(3, rig.Handler.Requests.Count);
        Assert.Equal("third", error.Message);
        Assert.Equal(503, error.Status);
        Assert.Equal(2, rig.Delays.Count);
    }

    [Fact]
    public async Task Zero_retries_means_one_attempt()
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Throw(new HttpRequestException("down")).Ok(Samples.Movement);

        var error = await Assert.ThrowsAsync<GameCoinException>(() => Grant(rig));

        Assert.Equal(GameCoinErrorCodes.NetworkError, error.Code);
        Assert.Single(rig.Handler.Requests);
        Assert.Empty(rig.Delays);
    }

    [Fact]
    public async Task Retry_after_wins_over_the_backoff_on_429()
    {
        var rig = new TestRig();
        rig.Handler
            .Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "Too many requests", null, ("Retry-After", "7"))
            .Ok(Samples.Movement);

        await Grant(rig);

        Assert.Equal(2, rig.Handler.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(7) }, rig.Delays);
    }

    [Fact]
    public async Task Retry_after_wins_on_5xx_too()
    {
        var rig = new TestRig();
        rig.Handler.Error(HttpStatusCode.ServiceUnavailable, "INTERNAL", "busy", null, ("Retry-After", "3")).Ok(Samples.Movement);

        await Grant(rig);

        Assert.Equal(new[] { TimeSpan.FromSeconds(3) }, rig.Delays);
    }

    [Fact]
    public async Task Retry_after_of_exactly_30_seconds_is_still_honoured()
    {
        var rig = new TestRig();
        rig.Handler.Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "slow down", null, ("Retry-After", "30")).Ok(Samples.Movement);

        await Grant(rig);

        Assert.Equal(new[] { TimeSpan.FromSeconds(30) }, rig.Delays);
    }

    [Fact]
    public async Task Retry_after_beyond_30_seconds_is_not_waited_for_and_is_raised()
    {
        var rig = new TestRig();
        rig.Handler.Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "slow down", null, ("Retry-After", "45")).Ok(Samples.Movement);

        var error = await Assert.ThrowsAsync<GameCoinException>(() => Grant(rig));

        Assert.Single(rig.Handler.Requests);
        Assert.Empty(rig.Delays);
        Assert.Equal(GameCoinErrorCodes.RateLimited, error.Code);
        Assert.Equal(429, error.Status);
        Assert.Equal(TimeSpan.FromSeconds(45), error.RetryAfter);
    }

    [Fact]
    public async Task Retry_after_as_an_http_date_in_the_past_means_retry_now()
    {
        var rig = new TestRig();
        rig.Handler
            .Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "slow down", null, ("Retry-After", "Wed, 21 Oct 2015 07:28:00 GMT"))
            .Ok(Samples.Movement);

        await Grant(rig);

        Assert.Equal(new[] { TimeSpan.Zero }, rig.Delays);
    }

    [Fact]
    public async Task Retry_after_as_an_http_date_in_the_future_is_converted_to_a_delay()
    {
        var rig = new TestRig();
        var when = DateTimeOffset.UtcNow.AddSeconds(20).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        rig.Handler.Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "slow down", null, ("Retry-After", when)).Ok(Samples.Movement);

        await Grant(rig);

        var delay = Assert.Single(rig.Delays);
        Assert.InRange(delay.TotalSeconds, 15, 20);
    }

    [Theory]
    [InlineData(400, "VALIDATION_FAILED")]
    [InlineData(400, "IDEMPOTENCY_KEY_REQUIRED")]
    [InlineData(401, "UNAUTHENTICATED")]
    [InlineData(402, "PAYMENT_FAILED")]
    [InlineData(403, "FORBIDDEN")]
    [InlineData(403, "PLAYER_BLOCKED")]
    [InlineData(404, "NOT_FOUND")]
    [InlineData(409, "INSUFFICIENT_FUNDS")]
    [InlineData(409, "LIMIT_REACHED")]
    [InlineData(409, "IDEMPOTENCY_CONFLICT")]
    [InlineData(409, "CONFLICT")]
    [InlineData(423, "READ_ONLY")]
    public async Task Never_retries_a_4xx_other_than_429(int status, string code)
    {
        var rig = new TestRig();
        rig.Handler.Error((HttpStatusCode)status, code, "nope").Ok(Samples.Movement);

        var error = await Assert.ThrowsAsync<GameCoinException>(() => Grant(rig));

        Assert.Single(rig.Handler.Requests);
        Assert.Empty(rig.Delays);
        Assert.Equal(code, error.Code);
        Assert.Equal(status, error.Status);
    }

    [Fact]
    public async Task Reads_are_retried_like_writes()
    {
        var rig = new TestRig();
        rig.Handler.Throw(new HttpRequestException("reset")).Ok(Samples.Catalog);

        var catalog = await rig.Client.Catalog.GetAsync();

        Assert.Equal(2, catalog.Currencies.Count);
        Assert.Equal(2, rig.Handler.Requests.Count);
    }

    [Fact]
    public async Task A_timeout_is_raised_with_status_zero_and_retried()
    {
        var rig = new TestRig(maxRetries: 1, timeout: TimeSpan.FromMilliseconds(80));
        rig.Handler.Hang().Ok(Samples.Movement);

        var movement = await Grant(rig);

        Assert.Equal(100, movement.Balance.Total);
        Assert.Equal(2, rig.Handler.Requests.Count);
    }

    [Fact]
    public async Task A_timeout_that_persists_is_raised_as_TIMEOUT()
    {
        var rig = new TestRig(maxRetries: 1, timeout: TimeSpan.FromMilliseconds(80));
        rig.Handler.Hang().Hang();

        var error = await Assert.ThrowsAsync<GameCoinException>(() => Grant(rig));

        Assert.Equal(GameCoinErrorCodes.Timeout, error.Code);
        Assert.Equal(0, error.Status);
        Assert.Equal(2, rig.Handler.Requests.Count);
    }

    [Fact]
    public async Task A_cancelled_call_raises_OperationCanceledException_and_is_not_retried()
    {
        var rig = new TestRig();
        rig.Handler.Hang().Ok(Samples.Movement);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            rig.Client.Wallet.GrantAsync(User, new GrantRequest { Currency = "gems", Amount = 1 }, cancellationToken: cts.Token));

        Assert.Single(rig.Handler.Requests);
    }

    [Fact]
    public async Task An_already_cancelled_token_sends_nothing()
    {
        var rig = new TestRig();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Client.Catalog.GetAsync(cts.Token));

        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public async Task A_5xx_with_an_html_body_is_retried_then_raised_as_INVALID_RESPONSE()
    {
        var rig = new TestRig(maxRetries: 1);
        rig.Handler.Raw(HttpStatusCode.BadGateway, "<html>bad gateway</html>").Raw(HttpStatusCode.BadGateway, "<html>bad gateway</html>");

        var error = await Assert.ThrowsAsync<GameCoinException>(() => Grant(rig));

        Assert.Equal(2, rig.Handler.Requests.Count);
        Assert.Equal(GameCoinErrorCodes.InvalidResponse, error.Code);
        Assert.Equal(502, error.Status);
    }

    // --- refund: no idempotency key, so only 429 is retried ---------------------------------------------------------------

    [Fact]
    public async Task Refund_is_retried_on_429()
    {
        var rig = new TestRig();
        rig.Handler
            .Error(HttpStatusCode.TooManyRequests, "RATE_LIMITED", "slow down", null, ("Retry-After", "2"))
            .Ok(Samples.Order);

        var order = await rig.Client.Orders.RefundAsync("o1");

        Assert.Equal("o1", order.Id);
        Assert.Equal(2, rig.Handler.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, rig.Delays);
    }

    [Fact]
    public async Task Refund_is_not_retried_after_a_network_error()
    {
        var rig = new TestRig();
        rig.Handler.Throw(new HttpRequestException("connection reset")).Ok(Samples.Order);

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Orders.RefundAsync("o1"));

        Assert.Equal(GameCoinErrorCodes.NetworkError, error.Code);
        Assert.Single(rig.Handler.Requests);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public async Task Refund_is_not_retried_after_a_5xx(int status)
    {
        var rig = new TestRig();
        rig.Handler.Error((HttpStatusCode)status, "INTERNAL", "oops").Ok(Samples.Order);

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Orders.RefundAsync("o1"));

        Assert.Equal(status, error.Status);
        Assert.Single(rig.Handler.Requests);
        Assert.Empty(rig.Delays);
    }

    [Fact]
    public async Task Refund_is_not_retried_after_a_timeout()
    {
        var rig = new TestRig(timeout: TimeSpan.FromMilliseconds(80));
        rig.Handler.Hang().Ok(Samples.Order);

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Orders.RefundAsync("o1"));

        Assert.Equal(GameCoinErrorCodes.Timeout, error.Code);
        Assert.Single(rig.Handler.Requests);
    }

    [Fact]
    public async Task Refund_conflict_is_raised_as_is()
    {
        var rig = new TestRig();
        rig.Handler.Error(HttpStatusCode.Conflict, "CONFLICT", "Order is not paid");

        var error = await Assert.ThrowsAsync<GameCoinException>(() => rig.Client.Orders.RefundAsync("o1"));

        Assert.Equal(GameCoinErrorCodes.Conflict, error.Code);
        Assert.Single(rig.Handler.Requests);
    }
}
