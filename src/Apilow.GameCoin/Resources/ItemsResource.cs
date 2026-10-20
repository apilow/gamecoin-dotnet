using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Apilow.GameCoin.Internal;

namespace Apilow.GameCoin;

/// <summary>The items of the game: <c>/items</c>. Each environment has its own catalog.</summary>
public sealed class ItemsResource
{
    private readonly ApiClient _api;

    internal ItemsResource(ApiClient api) => _api = api;

    /// <summary>
    /// Creates an item. A taken sku is <see cref="GameCoinErrorCodes.Conflict"/>; an unknown price currency, or <c>ClientPurchasable</c> without a price, is <see cref="GameCoinErrorCodes.ValidationFailed"/>.
    /// A create has no idempotency key (the sku is its natural key), so the SDK retries it on <c>429</c> only; after a
    /// network error, a timeout or a <c>5xx</c> the outcome is unknown: read the resource back with <see cref="GetAsync"/>.
    /// </summary>
    /// <param name="request">The new resource.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">A field is missing or malformed.</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<ManagedItem> CreateAsync(CreateItemRequest request, CancellationToken cancellationToken = default)
    {
        var body = ManagedBodies.CreateItem(request);
        var response = await _api.SendAsync(
            HttpMethod.Post, "/items", Responses.ManagedItem, cancellationToken, body: body, retryPolicy: RetryPolicy.RateLimitOnly).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>Gets an item, archived ones included. An unknown sku is <see cref="GameCoinErrorCodes.NotFound"/>.</summary>
    /// <param name="sku">The sku.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">The sku is empty.</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<ManagedItem> GetAsync(string sku, CancellationToken cancellationToken = default)
    {
        var path = Path(sku);
        var response = await _api.SendAsync(HttpMethod.Get, path, Responses.ManagedItem, cancellationToken).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>
    /// Changes an item: a <c>null</c> property is left unchanged. The sku is final. The type is final too. An archived resource cannot be
    /// changed (<see cref="GameCoinErrorCodes.Conflict"/>). A <c>null</c> request changes nothing.
    /// </summary>
    /// <param name="sku">The sku.</param>
    /// <param name="request">What to change.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">The sku is empty, or a field is malformed.</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<ManagedItem> UpdateAsync(string sku, UpdateItemRequest? request, CancellationToken cancellationToken = default)
    {
        var path = Path(sku);
        var body = ManagedBodies.UpdateItem(request);
        var response = await _api.SendAsync(HttpMethod.Patch, path, Responses.ManagedItem, cancellationToken, body: body).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>
    /// Archives an item (nothing is deleted) and returns it. Inventories keep it. It is <see cref="GameCoinErrorCodes.Conflict"/> (<c>details.blockedBy</c>) while an active pack contains it. Archiving an archived resource has no effect.
    /// </summary>
    /// <param name="sku">The sku.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">The sku is empty.</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<ManagedItem> ArchiveAsync(string sku, CancellationToken cancellationToken = default)
    {
        var path = Path(sku);
        var response = await _api.SendAsync(HttpMethod.Delete, path, Responses.ManagedItem, cancellationToken).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>The items of the game, in the order of the API; the archived ones only with <paramref name="includeArchived"/>.</summary>
    /// <param name="includeArchived">Also list the archived resources.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<IReadOnlyList<ManagedItem>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var response = await _api.SendAsync(
            HttpMethod.Get, "/items", Responses.ItemListResponse, cancellationToken, query: ManagedBodies.ListQuery(includeArchived)).ConfigureAwait(false);
        return response.Value.Items;
    }

    private static string Path(string sku) =>
        "/items/" + Uri.EscapeDataString(Guard.NotEmpty(sku, "The sku", nameof(sku)));
}
