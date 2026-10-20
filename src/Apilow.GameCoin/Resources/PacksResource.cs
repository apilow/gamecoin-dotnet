using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Apilow.GameCoin.Internal;

namespace Apilow.GameCoin;

/// <summary>The packs of the game: <c>/packs</c>. The catalog is shared by the <c>test</c> and <c>live</c> environments.</summary>
public sealed class PacksResource
{
    private readonly ApiClient _api;

    internal PacksResource(ApiClient api) => _api = api;

    /// <summary>
    /// Creates a pack. A taken sku is <see cref="GameCoinErrorCodes.Conflict"/>; an invalid pack (no grant and no item, an unknown or archived currency or item, a paid amount in a free currency) is <see cref="GameCoinErrorCodes.ValidationFailed"/> with the faulty lines in <c>details.fieldErrors</c>.
    /// A create has no idempotency key (the sku is its natural key), so the SDK retries it on <c>429</c> only; after a
    /// network error, a timeout or a <c>5xx</c> the outcome is unknown: read the resource back with <see cref="GetAsync"/>.
    /// </summary>
    /// <param name="request">The new resource.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">A field is missing or malformed.</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<ManagedPack> CreateAsync(CreatePackRequest request, CancellationToken cancellationToken = default)
    {
        var body = ManagedBodies.CreatePack(request);
        var response = await _api.SendAsync(
            HttpMethod.Post, "/packs", Responses.ManagedPack, cancellationToken, body: body, retryPolicy: RetryPolicy.RateLimitOnly).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>Gets a pack, archived ones included. An unknown sku is <see cref="GameCoinErrorCodes.NotFound"/>.</summary>
    /// <param name="sku">The sku.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">The sku is empty.</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<ManagedPack> GetAsync(string sku, CancellationToken cancellationToken = default)
    {
        var path = Path(sku);
        var response = await _api.SendAsync(HttpMethod.Get, path, Responses.ManagedPack, cancellationToken).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>
    /// Changes a pack: a <c>null</c> property is left unchanged. The sku is final. <c>Grants</c>, <c>Items</c> and <c>Availability</c> replace the previous value as a whole. An archived resource cannot be
    /// changed (<see cref="GameCoinErrorCodes.Conflict"/>). A <c>null</c> request changes nothing.
    /// </summary>
    /// <param name="sku">The sku.</param>
    /// <param name="request">What to change.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">The sku is empty, or a field is malformed.</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<ManagedPack> UpdateAsync(string sku, UpdatePackRequest? request, CancellationToken cancellationToken = default)
    {
        var path = Path(sku);
        var body = ManagedBodies.UpdatePack(request);
        var response = await _api.SendAsync(HttpMethod.Patch, path, Responses.ManagedPack, cancellationToken, body: body).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>
    /// Archives a pack (nothing is deleted) and returns it. The pack is no longer sold; the orders already placed are unaffected. Archiving an archived resource has no effect.
    /// </summary>
    /// <param name="sku">The sku.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">The sku is empty.</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<ManagedPack> ArchiveAsync(string sku, CancellationToken cancellationToken = default)
    {
        var path = Path(sku);
        var response = await _api.SendAsync(HttpMethod.Delete, path, Responses.ManagedPack, cancellationToken).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>The packs of the game, in the order of the API; the archived ones only with <paramref name="includeArchived"/>.</summary>
    /// <param name="includeArchived">Also list the archived resources.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<IReadOnlyList<ManagedPack>> ListAsync(bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        var response = await _api.SendAsync(
            HttpMethod.Get, "/packs", Responses.PackListResponse, cancellationToken, query: ManagedBodies.ListQuery(includeArchived)).ConfigureAwait(false);
        return response.Value.Packs;
    }

    private static string Path(string sku) =>
        "/packs/" + Uri.EscapeDataString(Guard.NotEmpty(sku, "The sku", nameof(sku)));
}
