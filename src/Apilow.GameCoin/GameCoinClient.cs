using System;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Apilow.GameCoin.Internal;

namespace Apilow.GameCoin;

/// <summary>
/// Client of the GameCoin server API. Create one per application and keep it: it is thread-safe and owns an
/// <see cref="HttpClient"/> (unless you pass yours). It authenticates with a <b>secret key</b> (<c>gc_sk_test_…</c> or
/// <c>gc_sk_live_…</c>) that must never reach a browser or a game client. The key is trimmed of surrounding white space, never
/// appears in an exception, and shows as its prefix only in <see cref="ToString"/>.
/// </summary>
/// <example>
/// <code>
/// using var gamecoin = new GameCoinClient(Environment.GetEnvironmentVariable("GAMECOIN_SECRET_KEY")!);
/// var movement = await gamecoin.Wallet.GrantAsync(PlayerRef.Ext("user-42"), new GrantRequest { Currency = "gems", Amount = 100 });
/// </code>
/// </example>
public sealed class GameCoinClient : IDisposable
{
    /// <summary>The production base URL.</summary>
    public const string DefaultBaseUrl = "https://gamecoin.apilow.com";

    private readonly HttpClient? _ownedHttpClient;
    private readonly string _maskedKey;

    /// <summary>Creates a client.</summary>
    /// <param name="secretKey">The secret key of the game and environment (<c>gc_sk_test_…</c> or <c>gc_sk_live_…</c>).</param>
    /// <param name="options">Base URL, timeout, retries, HTTP client; <c>null</c> for the defaults.</param>
    /// <exception cref="ArgumentException">
    /// The key is not a secret key (a publishable key <c>gc_pk_…</c> belongs to the browser SDK), or an option is invalid.
    /// The key itself is never echoed in the message.
    /// </exception>
    public GameCoinClient(string secretKey, GameCoinOptions? options = null)
    {
        options ??= new GameCoinOptions();
        var key = secretKey?.Trim(); // a key pasted from a file often carries a trailing newline
        var prefix = SecretKeyPrefix(key);
        Environment = prefix == "gc_sk_test_" ? "test" : "live";
        _maskedKey = prefix + "…";

        if (options.HttpClient is not null && options.HttpMessageHandler is not null)
        {
            throw new ArgumentException("Set either HttpClient or HttpMessageHandler, not both.", nameof(options));
        }

        if (options.MaxRetries is < 0 or > 10)
        {
            throw new ArgumentException("MaxRetries must be an integer from 0 to 10.", nameof(options));
        }

        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Timeout must be positive.", nameof(options));
        }

        BaseUrl = NormalizeBaseUrl(options.BaseUrl);

        HttpClient http;
        if (options.HttpClient is not null)
        {
            http = options.HttpClient;
        }
        else if (options.HttpMessageHandler is not null)
        {
            http = _ownedHttpClient = new HttpClient(options.HttpMessageHandler, disposeHandler: false) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        }
        else
        {
            http = _ownedHttpClient = new HttpClient(CreateDefaultHandler()) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        }

        var api = new ApiClient(http, BaseUrl, key!, options.Timeout, options.MaxRetries, BuildUserAgent(), options.DelayAsync, options.Jitter);
        Catalog = new CatalogResource(api);
        Players = new PlayersResource(api);
        Wallet = new WalletResource(api);
        Ledger = new LedgerResource(api);
        Inventory = new InventoryResource(api);
        Purchases = new PurchasesResource(api);
        Checkouts = new CheckoutsResource(api);
        Codes = new CodesResource(api);
        Orders = new OrdersResource(api);
    }

    /// <summary>The version of this SDK.</summary>
    public static string SdkVersion { get; } = ReadVersion();

    /// <summary><c>"test"</c> or <c>"live"</c>, read from the prefix of the secret key.</summary>
    public string Environment { get; }

    /// <summary>The base URL in use, without a trailing slash.</summary>
    public string BaseUrl { get; }

    /// <summary>The catalog of the game.</summary>
    public CatalogResource Catalog { get; }

    /// <summary>Players.</summary>
    public PlayersResource Players { get; }

    /// <summary>Wallets: balances, grants and spends.</summary>
    public WalletResource Wallet { get; }

    /// <summary>The ledger of a player.</summary>
    public LedgerResource Ledger { get; }

    /// <summary>Inventories.</summary>
    public InventoryResource Inventory { get; }

    /// <summary>Purchases of items with a currency.</summary>
    public PurchasesResource Purchases { get; }

    /// <summary>Checkouts: packs bought with real money.</summary>
    public CheckoutsResource Checkouts { get; }

    /// <summary>Gift codes: redeem a code for a player.</summary>
    public CodesResource Codes { get; }

    /// <summary>Orders and refunds.</summary>
    public OrdersResource Orders { get; }

    /// <summary>Releases the <see cref="HttpClient"/> the client created (an injected one is left alone).</summary>
    public void Dispose() => _ownedHttpClient?.Dispose();

    /// <summary>Describes the client; the secret key shows as its prefix only (<c>gc_sk_test_…</c>).</summary>
    public override string ToString() => $"GameCoinClient {{ Environment = {Environment}, BaseUrl = {BaseUrl}, SecretKey = {_maskedKey} }}";

    /// <summary>The prefix (<c>gc_sk_test_</c> or <c>gc_sk_live_</c>) of a valid secret key; throws without echoing the key otherwise.</summary>
    private static string SecretKeyPrefix(string? key)
    {
        if (key is not null)
        {
            foreach (var prefix in new[] { "gc_sk_test_", "gc_sk_live_" })
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal) && key.Length > prefix.Length)
                {
                    return prefix;
                }
            }

            if (key.StartsWith("gc_pk_", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "This is a publishable key (gc_pk_...), which belongs in the browser SDK. The server SDK needs a secret key (gc_sk_test_... or gc_sk_live_...).",
                    "secretKey");
            }
        }

        throw new ArgumentException("The server SDK needs a secret key (gc_sk_test_... or gc_sk_live_...).", "secretKey");
    }

    /// <summary>The origin of the service plus an optional path prefix, without trailing slashes.</summary>
    private static string NormalizeBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)
            || !Uri.TryCreate(baseUrl!.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.Query.Length > 1
            || uri.Fragment.Length > 1
            || uri.UserInfo.Length > 0)
        {
            throw new ArgumentException("BaseUrl must be an absolute http(s) URL without credentials, query or fragment, such as https://gamecoin.apilow.com.", "options");
        }

        return uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath.TrimEnd('/');
    }

    private static HttpMessageHandler CreateDefaultHandler()
    {
        // A redirect would silently turn a POST into a GET: surface it as an error instead.
#if NET5_0_OR_GREATER
        return new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(2) };
#else
        return new HttpClientHandler { AllowAutoRedirect = false };
#endif
    }

    private static string ReadVersion()
    {
        var informational = typeof(GameCoinClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = informational ?? typeof(GameCoinClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var plus = version.IndexOf('+');
        return plus >= 0 ? version.Substring(0, plus) : version;
    }

    private static string BuildUserAgent()
    {
        // ".NET 8.0.23" -> "dotnet/8.0.23"; "Mono 6.12.0" -> "mono/6.12.0"
        var framework = RuntimeInformation.FrameworkDescription.Trim();
        var split = framework.LastIndexOf(' ');
        var name = split > 0 ? framework.Substring(0, split) : framework;
        var number = split > 0 ? framework.Substring(split + 1) : "unknown";
        var runtime = name.Replace(".NET", "dotnet", StringComparison.Ordinal).Replace(' ', '-').ToLowerInvariant();
        return string.Format(CultureInfo.InvariantCulture, "gamecoin-dotnet/{0} ({1}/{2})", SdkVersion, runtime, number);
    }
}
