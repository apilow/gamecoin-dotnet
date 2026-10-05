using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Apilow.GameCoin.Internal;

namespace Apilow.GameCoin;

/// <summary>Orders: <c>GET /orders/{orderId}</c> and <c>POST /orders/{orderId}/refund</c>.</summary>
public sealed class OrdersResource
{
    private readonly ApiClient _api;

    internal OrdersResource(ApiClient api) => _api = api;

    /// <summary>Gets an order.</summary>
    /// <param name="orderId">The order id (<see cref="Order.Id"/>).</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">The order id is empty.</exception>
    /// <exception cref="GameCoinException">The call failed (<see cref="GameCoinErrorCodes.NotFound"/> for an unknown order).</exception>
    public async Task<Order> GetAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var segment = OrderSegment(orderId);
        var response = await _api.SendAsync(HttpMethod.Get, "/orders/" + segment, Responses.Order, cancellationToken).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>
    /// Refunds an order: the coins and the items it delivered are taken back. There is no idempotency key on this call, so
    /// the SDK retries it on <c>429</c> only; after a network error, a timeout or a <c>5xx</c> it raises, and you should check
    /// the outcome with <see cref="GetAsync"/> before trying again. Refunding an unpaid order is
    /// <see cref="GameCoinErrorCodes.Conflict"/>.
    /// </summary>
    /// <param name="orderId">The order id.</param>
    /// <param name="request">An optional reason.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">The order id is empty.</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<Order> RefundAsync(string orderId, RefundRequest? request = null, CancellationToken cancellationToken = default)
    {
        var segment = OrderSegment(orderId);
        var body = BodyWriter.Write(writer => writer.WriteIfNotNull("reason", request?.Reason));
        var response = await _api.SendAsync(
            HttpMethod.Post,
            "/orders/" + segment + "/refund",
            Responses.Order,
            cancellationToken,
            body: body,
            retryPolicy: RetryPolicy.RateLimitOnly).ConfigureAwait(false);
        return response.Value;
    }

    private static string OrderSegment(string orderId) =>
        Uri.EscapeDataString(Guard.NotEmpty(orderId, "The order id", nameof(orderId)));
}
