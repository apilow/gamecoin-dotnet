using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Apilow.GameCoin.Internal;

namespace Apilow.GameCoin;

/// <summary>Checkouts, the purchase of a pack with real money: <c>POST /players/{player}/checkouts</c>.</summary>
public sealed class CheckoutsResource
{
    private readonly ApiClient _api;

    internal CheckoutsResource(ApiClient api) => _api = api;

    /// <summary>
    /// Creates an order for a pack and the hosted payment page to send the player to. Idempotent: the call carries an
    /// <c>Idempotency-Key</c>, generated once per call unless <paramref name="options"/> gives one. A replay returns the order
    /// in its current state.
    /// </summary>
    /// <param name="player">The player (created if it is an unknown <c>ext:</c> reference).</param>
    /// <param name="request">Pack sku, return URLs, country, locale and creator code.</param>
    /// <param name="options">Per-call options, notably your own idempotency key.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">An argument is invalid (empty player or pack sku).</exception>
    /// <exception cref="GameCoinException">
    /// The call failed: <see cref="GameCoinErrorCodes.NotFound"/> for an unknown pack, <see cref="GameCoinErrorCodes.LimitReached"/>,
    /// <see cref="GameCoinErrorCodes.PackNotAvailable"/> (outside the sale window), <see cref="GameCoinErrorCodes.PackSoldOut"/>, or
    /// <see cref="GameCoinErrorCodes.ValidationFailed"/> with <c>fieldErrors.creatorCode = ["CODE_INVALID"]</c> for a refused creator code.
    /// </exception>
    public async Task<Checkout> CreateAsync(PlayerRef player, CheckoutRequest request, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var segment = Guard.PlayerSegment(player);
        Guard.NotNull(request, nameof(request));
        Guard.NotEmpty(request.PackSku, "PackSku", nameof(request));
        var successUrl = Guard.NotEmptyIfGiven(request.SuccessUrl, "SuccessUrl", nameof(request));
        var cancelUrl = Guard.NotEmptyIfGiven(request.CancelUrl, "CancelUrl", nameof(request));
        var country = Guard.NotEmptyIfGiven(request.Country, "Country", nameof(request));
        var locale = Guard.NotEmptyIfGiven(request.Locale, "Locale", nameof(request));
        var creatorCode = Guard.NotEmptyIfGiven(request.CreatorCode, "CreatorCode", nameof(request));
        var key = ApiClient.ResolveIdempotencyKey(options);

        var body = BodyWriter.Write(writer =>
        {
            writer.WriteString("packSku", request.PackSku);
            writer.WriteIfNotNull("successUrl", successUrl);
            writer.WriteIfNotNull("cancelUrl", cancelUrl);
            writer.WriteIfNotNull("country", country);
            writer.WriteIfNotNull("locale", locale);
            writer.WriteIfNotNull("creatorCode", creatorCode);
        });

        var response = await _api.SendAsync(
            HttpMethod.Post,
            "/players/" + segment + "/checkouts",
            Responses.Checkout,
            cancellationToken,
            body: body,
            idempotencyKey: key).ConfigureAwait(false);
        return response.Value with { Replayed = response.Replayed };
    }
}
