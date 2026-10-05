using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Apilow.GameCoin.Internal;

namespace Apilow.GameCoin;

/// <summary>Purchases of items with their currency price: <c>POST /players/{player}/purchases</c>.</summary>
public sealed class PurchasesResource
{
    private readonly ApiClient _api;

    internal PurchasesResource(ApiClient api) => _api = api;

    /// <summary>
    /// Buys an item for a player with its currency price: the currency is spent and the item is added to the inventory.
    /// Idempotent: the call carries an <c>Idempotency-Key</c>, generated once per call unless <paramref name="options"/> gives one.
    /// </summary>
    /// <param name="player">The player (created if it is an unknown <c>ext:</c> reference).</param>
    /// <param name="request">Item sku and quantity (default 1).</param>
    /// <param name="options">Per-call options, notably your own idempotency key.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">An argument is invalid (empty player or sku, quantity below 1).</exception>
    /// <exception cref="GameCoinException">
    /// The call failed (<see cref="GameCoinErrorCodes.InsufficientFunds"/>, <see cref="GameCoinErrorCodes.LimitReached"/>...).
    /// </exception>
    public async Task<Purchase> CreateAsync(PlayerRef player, PurchaseRequest request, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var segment = Guard.PlayerSegment(player);
        var body = ItemBody.Write(request);
        var key = ApiClient.ResolveIdempotencyKey(options);

        var response = await _api.SendAsync(
            HttpMethod.Post,
            "/players/" + segment + "/purchases",
            Responses.Purchase,
            cancellationToken,
            body: body,
            idempotencyKey: key).ConfigureAwait(false);
        return response.Value with { Replayed = response.Replayed };
    }
}
