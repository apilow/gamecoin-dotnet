using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Apilow.GameCoin.Internal;

namespace Apilow.GameCoin;

/// <summary>The currencies of the game: <c>/currencies</c>. The catalog is shared by the <c>test</c> and <c>live</c> environments.</summary>
public sealed class CurrenciesResource
{
    private readonly ApiClient _api;

    internal CurrenciesResource(ApiClient api) => _api = api;

    /// <summary>
    /// Creates a currency. A taken code (archived currencies keep theirs) is <see cref="GameCoinErrorCodes.Conflict"/>.
    /// A create has no idempotency key (the code is its natural key), so the SDK retries it on <c>429</c> only; after a
    /// network error, a timeout or a <c>5xx</c> the outcome is unknown: read the resource back with <see cref="GetAsync"/>.
    /// </summary>
    /// <param name="request">The new resource.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">A field is missing or malformed.</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<ManagedCurrency> CreateAsync(CreateCurrencyRequest request, CancellationToken cancellationToken = default)
    {
        var body = ManagedBodies.CreateCurrency(request);
        var response = await _api.SendAsync(
            HttpMethod.Post, "/currencies", Responses.ManagedCurrency, cancellationToken, body: body, retryPolicy: RetryPolicy.RateLimitOnly).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>Gets a currency, archived ones included. An unknown code is <see cref="GameCoinErrorCodes.NotFound"/>.</summary>
    /// <param name="code">The code.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">The code is empty.</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<ManagedCurrency> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        var path = Path(code);
        var response = await _api.SendAsync(HttpMethod.Get, path, Responses.ManagedCurrency, cancellationToken).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>
    /// Changes a currency: a <c>null</c> property is left unchanged. The code is final. <c>Purchasable</c> cannot become <c>false</c> while a pack that is not archived sells the currency for money. An archived resource cannot be
    /// changed (<see cref="GameCoinErrorCodes.Conflict"/>). A <c>null</c> request changes nothing.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="request">What to change.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">The code is empty, or a field is malformed.</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<ManagedCurrency> UpdateAsync(string code, UpdateCurrencyRequest? request, CancellationToken cancellationToken = default)
    {
        var path = Path(code);
        var body = ManagedBodies.UpdateCurrency(request);
        var response = await _api.SendAsync(HttpMethod.Patch, path, Responses.ManagedCurrency, cancellationToken, body: body).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>
    /// Archives a currency (nothing is deleted) and returns it. It is <see cref="GameCoinErrorCodes.Conflict"/> (<c>details.blockedBy</c>) while an active pack credits it or an active item is priced in it. Archiving an archived resource has no effect.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">The code is empty.</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<ManagedCurrency> ArchiveAsync(string code, CancellationToken cancellationToken = default)
    {
        var path = Path(code);
        var response = await _api.SendAsync(HttpMethod.Delete, path, Responses.ManagedCurrency, cancellationToken).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>The currencies of the game, in the order of the API; the archived ones only with <paramref name="includeArchived"/>.</summary>
    /// <param name="includeArchived">Also list the archived resources.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<IReadOnlyList<ManagedCurrency>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var response = await _api.SendAsync(
            HttpMethod.Get, "/currencies", Responses.CurrencyListResponse, cancellationToken, query: ManagedBodies.ListQuery(includeArchived)).ConfigureAwait(false);
        return response.Value.Currencies;
    }

    private static string Path(string code) =>
        "/currencies/" + Uri.EscapeDataString(Guard.NotEmpty(code, "The code", nameof(code)));
}
