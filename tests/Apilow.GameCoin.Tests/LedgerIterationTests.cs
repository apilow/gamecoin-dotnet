using System.Net;

namespace Apilow.GameCoin.Tests;

public class LedgerIterationTests
{
    private static readonly PlayerRef User = PlayerRef.Ext("user-42");

    private static string Entry(string id) =>
        $$"""{"id":"{{id}}","type":"grant","currency":"gems","paidDelta":0,"bonusDelta":1,"balanceAfter":{"paid":0,"bonus":1,"total":1},"reason":null,"metadata":{},"ref":{},"createdAt":"2026-10-05T20:38:27.227Z"}""";

    private static string Page(string? nextCursor, params string[] ids) =>
        $$"""{"entries":[{{string.Join(",", ids.Select(Entry))}}],"nextCursor":{{(nextCursor is null ? "null" : $"\"{nextCursor}\"")}}}""";

    [Fact]
    public async Task Iterates_across_several_pages_in_order()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Page("c1", "e1", "e2")).Ok(Page("c2", "e3", "e4")).Ok(Page(null, "e5"));

        var ids = new List<string>();
        await foreach (var entry in rig.Client.Ledger.IterateAsync(User, new LedgerQuery { Currency = "gems", Limit = 2 }))
        {
            ids.Add(entry.Id);
        }

        Assert.Equal(new[] { "e1", "e2", "e3", "e4", "e5" }, ids);
        Assert.Equal(3, rig.Handler.Requests.Count);
        Assert.Equal("/api/v1/players/ext%3Auser-42/ledger?currency=gems&limit=2", rig.Handler.Requests[0].PathAndQuery);
        Assert.Equal("/api/v1/players/ext%3Auser-42/ledger?currency=gems&limit=2&cursor=c1", rig.Handler.Requests[1].PathAndQuery);
        Assert.Equal("/api/v1/players/ext%3Auser-42/ledger?currency=gems&limit=2&cursor=c2", rig.Handler.Requests[2].PathAndQuery);
    }

    [Fact]
    public async Task Fetches_pages_lazily()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Page("c1", "e1", "e2")).Ok(Page(null, "e3"));

        await using var enumerator = rig.Client.Ledger.IterateAsync(User).GetAsyncEnumerator();
        Assert.Empty(rig.Handler.Requests); // nothing before the first MoveNext

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("e1", enumerator.Current.Id);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("e2", enumerator.Current.Id);
        Assert.Single(rig.Handler.Requests); // the second page is not requested while the first is being consumed

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("e3", enumerator.Current.Id);
        Assert.Equal(2, rig.Handler.Requests.Count);
        Assert.False(await enumerator.MoveNextAsync());
        Assert.Equal(2, rig.Handler.Requests.Count);
    }

    [Fact]
    public async Task Stopping_early_requests_no_further_page()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Page("c1", "e1", "e2")).Ok(Page(null, "e3"));

        var first = new List<string>();
        await foreach (var entry in rig.Client.Ledger.IterateAsync(User))
        {
            first.Add(entry.Id);
            if (first.Count == 2)
            {
                break;
            }
        }

        Assert.Equal(new[] { "e1", "e2" }, first);
        Assert.Single(rig.Handler.Requests);
    }

    [Fact]
    public async Task An_empty_ledger_yields_nothing_after_one_request()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Page(null));

        var count = 0;
        await foreach (var _ in rig.Client.Ledger.IterateAsync(User))
        {
            count++;
        }

        Assert.Equal(0, count);
        Assert.Single(rig.Handler.Requests);
    }

    [Fact]
    public async Task A_starting_cursor_resumes_from_that_page()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Page("c3", "e5")).Ok(Page(null, "e6"));

        var ids = new List<string>();
        await foreach (var entry in rig.Client.Ledger.IterateAsync(User, new LedgerQuery { Cursor = "c2" }))
        {
            ids.Add(entry.Id);
        }

        Assert.Equal(new[] { "e5", "e6" }, ids);
        Assert.EndsWith("ledger?cursor=c2", rig.Handler.Requests[0].PathAndQuery);
        Assert.EndsWith("ledger?cursor=c3", rig.Handler.Requests[1].PathAndQuery);
    }

    [Fact]
    public async Task An_error_on_a_later_page_is_raised_after_the_earlier_entries()
    {
        var rig = new TestRig(maxRetries: 0);
        rig.Handler.Ok(Page("c1", "e1")).Error(HttpStatusCode.BadRequest, "VALIDATION_FAILED", "bad cursor", """{"fieldErrors":{"cursor":["CURSOR_INVALID"]}}""");

        var ids = new List<string>();
        var error = await Assert.ThrowsAsync<GameCoinException>(async () =>
        {
            await foreach (var entry in rig.Client.Ledger.IterateAsync(User))
            {
                ids.Add(entry.Id);
            }
        });

        Assert.Equal(new[] { "e1" }, ids);
        Assert.Equal(GameCoinErrorCodes.ValidationFailed, error.Code);
    }

    [Fact]
    public async Task A_transient_failure_on_a_page_is_retried_transparently()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Page("c1", "e1")).Error(HttpStatusCode.ServiceUnavailable, "INTERNAL", "blip").Ok(Page(null, "e2"));

        var ids = new List<string>();
        await foreach (var entry in rig.Client.Ledger.IterateAsync(User))
        {
            ids.Add(entry.Id);
        }

        Assert.Equal(new[] { "e1", "e2" }, ids);
        Assert.Equal(3, rig.Handler.Requests.Count);
    }

    [Fact]
    public async Task Iteration_can_be_cancelled()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Page("c1", "e1")).Ok(Page(null, "e2"));
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var entry in rig.Client.Ledger.IterateAsync(User, cancellationToken: cts.Token))
            {
                cts.Cancel();
            }
        });

        Assert.Single(rig.Handler.Requests);
    }

    [Fact]
    public async Task WithCancellation_flows_into_the_requests()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Page(null, "e1"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in rig.Client.Ledger.IterateAsync(User).WithCancellation(cts.Token))
            {
            }
        });

        Assert.Empty(rig.Handler.Requests);
    }
}
