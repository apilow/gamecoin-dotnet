using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Apilow.GameCoin.Internal;

namespace Apilow.GameCoin;

/// <summary>Players: <c>PUT</c> and <c>GET /players/{player}</c>, <c>POST /players/{player}/tokens</c>.</summary>
public sealed class PlayersResource
{
    private readonly ApiClient _api;

    internal PlayersResource(ApiClient api) => _api = api;

    /// <summary>
    /// Creates the player if it does not exist (an <c>ext:</c> player is created on first use) and updates the fields of
    /// <paramref name="profile"/>. Calling it again with the same profile is harmless.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="profile">Fields to set or clear; <c>null</c> changes nothing.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="ArgumentException">A field is both set and cleared, or the player is empty.</exception>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<Player> UpsertAsync(PlayerRef player, PlayerProfile? profile = null, CancellationToken cancellationToken = default)
    {
        var segment = Guard.PlayerSegment(player);
        var body = profile is null ? BodyWriter.Empty() : WriteProfile(profile);
        var response = await _api.SendAsync(
            HttpMethod.Put, "/players/" + segment, Responses.Player, cancellationToken, body: body).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>Gets a player. An unknown player is <see cref="GameCoinErrorCodes.NotFound"/>, even for an <c>ext:</c> reference.</summary>
    /// <param name="player">The player.</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<Player> GetAsync(PlayerRef player, CancellationToken cancellationToken = default)
    {
        var segment = Guard.PlayerSegment(player);
        var response = await _api.SendAsync(HttpMethod.Get, "/players/" + segment, Responses.Player, cancellationToken).ConfigureAwait(false);
        return response.Value;
    }

    /// <summary>Creates a short-lived player token, to hand to the browser of the player (browser SDK).</summary>
    /// <param name="player">The player (created if it is an unknown <c>ext:</c> reference).</param>
    /// <param name="cancellationToken">Cancels the call (and its retries).</param>
    /// <exception cref="GameCoinException">The call failed.</exception>
    public async Task<PlayerToken> CreateTokenAsync(PlayerRef player, CancellationToken cancellationToken = default)
    {
        var segment = Guard.PlayerSegment(player);
        var response = await _api.SendAsync(
            HttpMethod.Post, "/players/" + segment + "/tokens", Responses.PlayerToken, cancellationToken).ConfigureAwait(false);
        return response.Value;
    }

    private static byte[] WriteProfile(PlayerProfile profile)
    {
        CheckNotBoth(profile.DisplayName is not null, PlayerProfileFields.DisplayName, profile.Clear);
        CheckNotBoth(profile.Email is not null, PlayerProfileFields.Email, profile.Clear);
        CheckNotBoth(profile.Country is not null, PlayerProfileFields.Country, profile.Clear);
        CheckNotBoth(profile.BirthYear is not null, PlayerProfileFields.BirthYear, profile.Clear);

        Guard.NotEmptyIfGiven(profile.DisplayName, "DisplayName", "profile");
        Guard.NotEmptyIfGiven(profile.Email, "Email", "profile");
        Guard.NotEmptyIfGiven(profile.Country, "Country", "profile");
        if (profile.BirthYear is { } birthYear)
        {
            Guard.Positive(birthYear, "BirthYear", "profile");
        }

        return BodyWriter.Write(writer =>
        {
            WriteField(writer, "displayName", profile.DisplayName, profile.Clear.HasFlag(PlayerProfileFields.DisplayName));
            WriteField(writer, "email", profile.Email, profile.Clear.HasFlag(PlayerProfileFields.Email));
            WriteField(writer, "country", profile.Country, profile.Clear.HasFlag(PlayerProfileFields.Country));
            if (profile.BirthYear is { } year)
            {
                writer.WriteNumber("birthYear", year);
            }
            else if (profile.Clear.HasFlag(PlayerProfileFields.BirthYear))
            {
                writer.WriteNull("birthYear");
            }
        });
    }

    private static void WriteField(Utf8JsonWriter writer, string name, string? value, bool clear)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
        else if (clear)
        {
            writer.WriteNull(name);
        }
    }

    private static void CheckNotBoth(bool isSet, PlayerProfileFields field, PlayerProfileFields clear)
    {
        if (isSet && clear.HasFlag(field))
        {
            throw new ArgumentException($"The profile field {field} cannot be both set and cleared.", "profile");
        }
    }
}
