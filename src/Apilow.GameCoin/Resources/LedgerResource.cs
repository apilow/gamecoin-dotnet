using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Apilow.GameCoin.Internal;

namespace Apilow.GameCoin;

/// <summary>The ledger of a player: <c>GET /players/{player}/ledger</c>.</summary>
public sealed class LedgerResource
{
    private readonly ApiClient _api;

    internal LedgerResource(ApiClient api) => _api = api;

    /// <summary>Gets one page of the ledger, newest entries first.</summary>
    /// <param name="player">The player.</param>
    /// <param name="query">Currency filter, page size and cursor; <c>null</c> for the first page with the defaults.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">An argument is invalid (empty player, limit outside 1 to 100).</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<LedgerPage> ListAsync(PlayerRef player, LedgerQuery? query = null, CancellationToken cancellationToken = default)
    {
        var segment = Guard.PlayerSegment(player);
        var response = await _api.SendAsync(
            HttpMethod.Get,
            "/players/" + segment + "/ledger",
            Responses.LedgerPage,
            cancellationToken,
            query: BuildQuery(query)).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>
    /// Iterates over every entry of the ledger, page after page, lazily: a page is fetched only when the previous one is
    /// exhausted. <see cref="LedgerQuery.Cursor"/> resumes from a given page.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="query">Currency filter and page size (the cursor is followed automatically).</param>
    /// <param name="cancellationToken">Cancels the iteration.</param>
    /// <exception cref="ArgumentException">An argument is invalid (empty player, limit outside 1 to 100).</exception>
    /// <exception cref="GameCoinException">A page could not be fetched.</exception>
    public IAsyncEnumerable<LedgerEntry> IterateAsync(
        PlayerRef player,
        LedgerQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        // Arguments are checked right away; requests start with the first MoveNextAsync.
        Guard.PlayerSegment(player);
        BuildQuery(query);
        return IteratePages(player, query, cancellationToken);
    }

    private async IAsyncEnumerable<LedgerEntry> IteratePages(
        PlayerRef player,
        LedgerQuery? query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cursor = query?.Cursor;
        do
        {
            var page = await ListAsync(player, (query ?? new LedgerQuery()) with { Cursor = cursor }, cancellationToken).ConfigureAwait(false);
            foreach (var entry in page.Entries)
            {
                yield return entry;
            }

            cursor = page.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));
    }

    private static string? BuildQuery(LedgerQuery? query)
    {
        if (query is null)
        {
            return null;
        }

        var text = new StringBuilder();

        void Add(string name, string value)
        {
            text.Append(text.Length == 0 ? '?' : '&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }

        if (Guard.NotEmptyIfGiven(query.Currency, "Currency", nameof(query)) is { } currency)
        {
            Add("currency", currency);
        }

        if (query.Limit is { } limit)
        {
            Guard.InRange(limit, 1, 100, "Limit", nameof(query));
            Add("limit", limit.ToString(CultureInfo.InvariantCulture));
        }

        if (Guard.NotEmptyIfGiven(query.Cursor, "Cursor", nameof(query)) is { } cursor)
        {
            Add("cursor", cursor);
        }

        return text.Length == 0 ? null : text.ToString();
    }
}
