using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Apilow.GameCoin.Internal;

namespace Apilow.GameCoin;

/// <summary>Wallets: <c>GET /players/{player}/wallet</c> and the grant and spend movements.</summary>
public sealed class WalletResource
{
    private readonly ApiClient _api;

    internal WalletResource(ApiClient api) => _api = api;

    /// <summary>Gets the balances of a player: one line per active currency, zeros included.</summary>
    /// <param name="player">The player.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="GameCoinException">The call failed (<see cref="GameCoinErrorCodes.NotFound"/> for an unknown player).</exception>
    public async Task<Wallet> GetAsync(PlayerRef player, CancellationToken cancellationToken = default)
    {
        var segment = Guard.PlayerSegment(player);
        var response = await _api.SendAsync(
            HttpMethod.Get, "/players/" + segment + "/wallet", Responses.Wallet, cancellationToken).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>
    /// Credits a currency to a player (always to the bonus bucket). Idempotent: the call carries an <c>Idempotency-Key</c>,
    /// generated once per call unless <paramref name="options"/> gives one, so the automatic retries of the SDK cannot credit twice.
    /// </summary>
    /// <param name="player">The player (created if it is an unknown <c>ext:</c> reference).</param>
    /// <param name="request">Currency, amount, optional reason and metadata.</param>
    /// <param name="options">Per-call options, notably your own idempotency key.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">An argument is invalid (empty player or currency, amount below 1).</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public Task<Movement> GrantAsync(PlayerRef player, GrantRequest request, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        MoveAsync(player, request, "/wallet/grant", options, cancellationToken);

    /// <summary>
    /// Debits a currency from a player. Idempotent like <see cref="GrantAsync"/>. A wallet too low raises
    /// <see cref="GameCoinErrorCodes.InsufficientFunds"/>, with <c>Details["required"]</c> and <c>Details["available"]</c>.
    /// </summary>
    /// <param name="player">The player (created if it is an unknown <c>ext:</c> reference).</param>
    /// <param name="request">Currency, amount, optional reason and metadata.</param>
    /// <param name="options">Per-call options, notably your own idempotency key.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">An argument is invalid (empty player or currency, amount below 1).</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public Task<Movement> SpendAsync(PlayerRef player, SpendRequest request, RequestOptions? options = null, CancellationToken cancellationToken = default) =>
        MoveAsync(player, request, "/wallet/spend", options, cancellationToken);

    private async Task<Movement> MoveAsync(
        PlayerRef player,
        WalletMovementRequest request,
        string action,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        var segment = Guard.PlayerSegment(player);
        Guard.NotNull(request, nameof(request));
        Guard.NotEmpty(request.Currency, "Currency", nameof(request));
        Guard.Positive(request.Amount, "Amount", nameof(request));
        var key = ApiClient.ResolveIdempotencyKey(options);

        var body = BodyWriter.Write(writer =>
        {
            writer.WriteString("currency", request.Currency);
            writer.WriteNumber("amount", request.Amount);
            writer.WriteIfNotNull("reason", request.Reason);
            writer.WriteMetadata(request.Metadata);
        });

        var response = await _api.SendAsync(
            HttpMethod.Post,
            "/players/" + segment + action,
            Responses.Movement,
            cancellationToken,
            body: body,
            idempotencyKey: key).ConfigureAwait(false);
        return response.Value with { Replayed = response.Replayed };
    }
}
