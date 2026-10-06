using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Apilow.GameCoin.Internal;

namespace Apilow.GameCoin;

/// <summary>Gift codes: <c>POST /players/{player}/codes/redeem</c>.</summary>
public sealed class CodesResource
{
    private readonly ApiClient _api;

    internal CodesResource(ApiClient api) => _api = api;

    /// <summary>
    /// Redeems a gift code for a player: free coins (the bonus bucket, never paid coins) and items, as the studio defined the
    /// code. Idempotent: the call carries an <c>Idempotency-Key</c>, generated once per call unless <paramref name="options"/>
    /// gives one; replaying a key returns the same <see cref="Redemption"/> and credits nothing twice.
    /// </summary>
    /// <param name="player">The player (created if it is an unknown <c>ext:</c> reference).</param>
    /// <param name="code">
    /// The code as the player typed it. The API ignores case, spaces and dashes: the SDK sends it untouched and only checks that
    /// it is not empty.
    /// </param>
    /// <param name="options">Per-call options, notably your own idempotency key.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <returns>What was credited and delivered; <see cref="Redemption.Replayed"/> tells whether it was a replay.</returns>
    /// <exception cref="ArgumentException">An argument is invalid (empty player or code).</exception>
    /// <exception cref="GameCoinException">
    /// The call failed. An unknown, expired, used up, disabled or already used code is always
    /// <see cref="GameCoinErrorCodes.ValidationFailed"/> with <c>fieldErrors.code = ["CODE_INVALID"]</c> (never retried);
    /// also <see cref="GameCoinErrorCodes.PlayerBlocked"/>, <see cref="GameCoinErrorCodes.LimitReached"/>,
    /// <see cref="GameCoinErrorCodes.Conflict"/> and <see cref="GameCoinErrorCodes.RateLimited"/>.
    /// </exception>
    public async Task<Redemption> RedeemAsync(PlayerRef player, string code, RequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        var segment = Guard.PlayerSegment(player);
        Guard.NotEmpty(code, "The code", nameof(code));
        var key = ApiClient.ResolveIdempotencyKey(options);

        var body = BodyWriter.Write(writer => writer.WriteString("code", code));

        var response = await _api.SendAsync(
            HttpMethod.Post,
            "/players/" + segment + "/codes/redeem",
            Responses.Redemption,
            cancellationToken,
            body: body,
            idempotencyKey: key).ConfigureAwait(false);
        return response.Value with { Replayed = response.Replayed };
    }
}
