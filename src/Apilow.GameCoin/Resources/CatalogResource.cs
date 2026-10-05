using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Apilow.GameCoin.Internal;

namespace Apilow.GameCoin;

/// <summary>The catalog of the game: <c>GET /catalog</c>.</summary>
public sealed class CatalogResource
{
    private readonly ApiClient _api;

    internal CatalogResource(ApiClient api) => _api = api;

    /// <summary>Gets the active currencies, items and packs of the game.</summary>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<Catalog> GetAsync(CancellationToken cancellationToken = default)
    {
        var response = await _api.SendAsync(HttpMethod.Get, "/catalog", Responses.Catalog, cancellationToken).ConfigureAwait(false);
        return response.Value;
    }
}
