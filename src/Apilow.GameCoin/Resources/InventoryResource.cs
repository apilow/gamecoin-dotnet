using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Apilow.GameCoin.Internal;

namespace Apilow.GameCoin;

/// <summary>The inventory of a player: <c>GET /players/{player}/inventory</c> and the grant and consume actions.</summary>
public sealed class InventoryResource
{
    private readonly ApiClient _api;

    internal InventoryResource(ApiClient api) => _api = api;

    /// <summary>Lists the items a player owns (quantity above 0).</summary>
    /// <param name="player">The player.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<IReadOnlyList<InventoryItem>> ListAsync(PlayerRef player, CancellationToken cancellationToken = default)
    {
        var segment = Guard.PlayerSegment(player);
        var response = await _api.SendAsync(
            HttpMethod.Get, "/players/" + segment + "/inventory", Responses.InventoryListResponse, cancellationToken).ConfigureAwait(false);
        return response.Value.Items;
    }

    /// <summary>
    /// Puts an item in the inventory of a player, for free. Idempotent: the call carries an <c>Idempotency-Key</c>, generated
    /// once per call unless <paramref name="options"/> gives one.
    /// </summary>
    /// <param name="player">The player (created if it is an unknown <c>ext:</c> reference).</param>
    /// <param name="request">Item sku and quantity (default 1).</param>
    /// <param name="options">Per-call options, notably your own idempotency key.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <returns>The item with its new quantity; <see cref="InventoryItem.Replayed"/> tells whether it was a replay.</returns>
    /// <exception cref="ArgumentException">An argument is invalid (empty player or sku, quantity below 1).</exception>
    /// <exception cref="GameCoinException">The call failed (<see cref="GameCoinErrorCodes.LimitReached"/> above <c>maxOwned</c>).</exception>
    public Task<InventoryItem> GrantAsync(PlayerRef player, InventoryGrantRequest request, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        ChangeAsync(player, request, "/inventory/grant", options, cancellationToken);

    /// <summary>
    /// Uses up an item of the inventory of a player. Idempotent like <see cref="GrantAsync"/>. The item is returned even when
    /// its quantity reaches 0.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="request">Item sku and quantity (default 1).</param>
    /// <param name="options">Per-call options, notably your own idempotency key.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <returns>The item with its new quantity; <see cref="InventoryItem.Replayed"/> tells whether it was a replay.</returns>
    /// <exception cref="ArgumentException">An argument is invalid (empty player or sku, quantity below 1).</exception>
    /// <exception cref="GameCoinException">The call failed (<see cref="GameCoinErrorCodes.InsufficientFunds"/> when the player owns too few).</exception>
    public Task<InventoryItem> ConsumeAsync(PlayerRef player, InventoryConsumeRequest request, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        ChangeAsync(player, request, "/inventory/consume", options, cancellationToken);

    private async Task<InventoryItem> ChangeAsync(
        PlayerRef player,
        ItemQuantityRequest request,
        string action,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        var segment = Guard.PlayerSegment(player);
        var body = ItemBody.Write(request);
        var key = ApiClient.ResolveIdempotencyKey(options);

        var response = await _api.SendAsync(
            HttpMethod.Post,
            "/players/" + segment + action,
            Responses.InventoryChangeResponse,
            cancellationToken,
            body: body,
            idempotencyKey: key).ConfigureAwait(false);
        return response.Value.Item with { Replayed = response.Replayed };
    }
}

internal static class ItemBody
{
    public static byte[] Write(ItemQuantityRequest request)
    {
        Guard.NotNull(request, nameof(request));
        Guard.NotEmpty(request.Sku, "Sku", nameof(request));
        Guard.Positive(request.Quantity, "Quantity", nameof(request));
        return BodyWriter.Write(writer =>
        {
            writer.WriteString("sku", request.Sku);
            writer.WriteNumber("quantity", request.Quantity);
        });
    }
}
