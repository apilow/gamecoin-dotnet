using System.Net;
using System.Net.Http;

namespace Apilow.GameCoin.Tests;

public class ClientTests
{
    [Fact]
    public async Task Sends_authentication_accept_and_user_agent_headers()
    {
        var rig = new TestRig();
        rig.Handler.Ok(Samples.Catalog);

        await rig.Client.Catalog.GetAsync();

        var request = rig.Only;
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://gc.example.test/api/v1/catalog", request.Uri.ToString());
        Assert.Equal("Bearer " + TestRig.SecretKey, request.Header("Authorization"));
        Assert.Equal("application/json", request.Header("Accept"));
        Assert.Matches(@"^gamecoin-dotnet/0\.1\.0 \(dotnet/\d+\.\d+(\.\d+)?\)$", request.Header("User-Agent"));
        Assert.False(request.HasHeader("Idempotency-Key"));
        Assert.False(request.HasHeader("Content-Type"));
        Assert.Equal("0.1.0", GameCoinClient.SdkVersion);
    }

    [Theory]
    [InlineData("gc_sk_test_abc123", "test")]
    [InlineData("gc_sk_live_abc123", "live")]
    public void Environment_is_read_from_the_key_prefix(string key, string expected)
    {
        using var client = new GameCoinClient(key);
        Assert.Equal(expected, client.Environment);
    }

    [Fact]
    public void Default_base_url_is_production()
    {
        using var client = new GameCoinClient("gc_sk_test_abc");
        Assert.Equal("https://gamecoin.apilow.com", client.BaseUrl);
        Assert.Equal(GameCoinClient.DefaultBaseUrl, client.BaseUrl);
    }

    [Fact]
    public void Rejects_a_publishable_key_and_points_to_the_browser_sdk()
    {
        const string publishable = "gc_pk_test_PUBLISHABLE0123456789";
        var error = Assert.Throws<ArgumentException>(() => new GameCoinClient(publishable));
        Assert.Contains("browser SDK", error.Message);
        Assert.DoesNotContain(publishable, error.Message);
        Assert.DoesNotContain(publishable, error.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("gc_pt_player_token")]
    [InlineData("sk_test_nope")]
    [InlineData("gc_sk_test_")]
    [InlineData("gc_sk_staging_abc")]
    public void Rejects_anything_that_is_not_a_secret_key(string key)
    {
        var error = Assert.Throws<ArgumentException>(() => new GameCoinClient(key));
        Assert.Contains("secret key", error.Message);
        if (key.Length > "gc_sk_test_".Length)
        {
            Assert.DoesNotContain(key, error.Message);
        }
    }

    [Fact]
    public void Rejects_a_null_key()
    {
        Assert.Throws<ArgumentException>(() => new GameCoinClient(null!));
    }

    [Theory]
    [InlineData("https://gc.example.test/", "https://gc.example.test")]
    [InlineData("https://gc.example.test///", "https://gc.example.test")]
    [InlineData("http://localhost:3400", "http://localhost:3400")]
    [InlineData("https://gateway.example.test/gamecoin/", "https://gateway.example.test/gamecoin")]
    public async Task Base_url_is_normalized_and_the_api_path_appended(string baseUrl, string expectedPrefix)
    {
        var rig = new TestRig(baseUrl: baseUrl);
        rig.Handler.Ok(Samples.Catalog);

        await rig.Client.Catalog.GetAsync();

        Assert.Equal(expectedPrefix, rig.Client.BaseUrl);
        Assert.Equal(expectedPrefix + "/api/v1/catalog", rig.Only.Uri.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("gamecoin.apilow.com")]
    [InlineData("ftp://gamecoin.apilow.com")]
    public void Rejects_an_invalid_base_url(string baseUrl)
    {
        Assert.Throws<ArgumentException>(() => new GameCoinClient("gc_sk_test_abc", new GameCoinOptions { BaseUrl = baseUrl }));
    }

    [Fact]
    public void Rejects_invalid_options()
    {
        Assert.Throws<ArgumentException>(() => new GameCoinClient("gc_sk_test_abc", new GameCoinOptions { MaxRetries = -1 }));
        Assert.Throws<ArgumentException>(() => new GameCoinClient("gc_sk_test_abc", new GameCoinOptions { Timeout = TimeSpan.Zero }));
        Assert.Throws<ArgumentException>(() => new GameCoinClient(
            "gc_sk_test_abc",
            new GameCoinOptions { HttpClient = new HttpClient(), HttpMessageHandler = new FakeHandler() }));
    }

    [Fact]
    public async Task An_injected_http_client_is_used_left_untouched_and_not_disposed()
    {
        var handler = new FakeHandler().Ok(Samples.Catalog);
        using var http = new HttpClient(handler);
        var client = new GameCoinClient("gc_sk_test_abc", new GameCoinOptions { BaseUrl = "https://gc.example.test", HttpClient = http });

        await client.Catalog.GetAsync();
        client.Dispose();

        Assert.Single(handler.Requests);
        Assert.False(handler.Disposed);
        Assert.Empty(http.DefaultRequestHeaders); // the secret key goes on each request, never on a shared client
        Assert.Equal("Bearer gc_sk_test_abc", handler.Requests[0].Header("Authorization"));
    }

    [Fact]
    public void An_injected_message_handler_is_not_disposed_with_the_client()
    {
        var handler = new FakeHandler();
        var client = new GameCoinClient("gc_sk_test_abc", new GameCoinOptions { HttpMessageHandler = handler });
        client.Dispose();
        Assert.False(handler.Disposed);
    }

    [Fact]
    public void ToString_describes_the_client_without_the_key()
    {
        var rig = new TestRig();
        var text = rig.Client.ToString();
        Assert.Contains("test", text);
        Assert.Contains("https://gc.example.test", text);
        Assert.Contains("gc_sk_test_…", text); // the prefix only
        Assert.DoesNotContain(TestRig.SecretKey, text);
        Assert.DoesNotContain("SECRETVALUE", text);
    }

    [Fact]
    public async Task Every_request_gets_a_fresh_message_so_a_client_is_reusable_and_thread_safe()
    {
        var rig = new TestRig();
        for (var i = 0; i < 20; i++)
        {
            rig.Handler.Ok(Samples.Catalog);
        }

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => rig.Client.Catalog.GetAsync()));

        Assert.Equal(20, rig.Handler.Requests.Count);
    }

    [Fact]
    public void Player_ref_helpers()
    {
        Assert.Equal("ext:user-42", PlayerRef.Ext("user-42").Value);
        Assert.Equal("6ac40ac0", PlayerRef.FromId("6ac40ac0").Value);
        PlayerRef implicitRef = "6ac40ac0";
        Assert.Equal(PlayerRef.FromId("6ac40ac0"), implicitRef);
        Assert.True(PlayerRef.Ext("a") == PlayerRef.Ext("a"));
        Assert.True(PlayerRef.Ext("a") != PlayerRef.Ext("b"));
        Assert.Equal("ext:a", PlayerRef.Ext("a").ToString());
        Assert.True(default(PlayerRef).IsEmpty);
        Assert.Throws<ArgumentException>(() => PlayerRef.Ext(""));
        Assert.Throws<ArgumentException>(() => PlayerRef.Ext(null!));
        Assert.Throws<ArgumentException>(() => PlayerRef.FromId(""));
    }

    [Fact]
    public void Error_code_constants_match_the_api_codes()
    {
        var expected = new Dictionary<string, string>
        {
            [nameof(GameCoinErrorCodes.ValidationFailed)] = "VALIDATION_FAILED",
            [nameof(GameCoinErrorCodes.IdempotencyKeyRequired)] = "IDEMPOTENCY_KEY_REQUIRED",
            [nameof(GameCoinErrorCodes.Unauthenticated)] = "UNAUTHENTICATED",
            [nameof(GameCoinErrorCodes.PaymentFailed)] = "PAYMENT_FAILED",
            [nameof(GameCoinErrorCodes.Forbidden)] = "FORBIDDEN",
            [nameof(GameCoinErrorCodes.PlayerBlocked)] = "PLAYER_BLOCKED",
            [nameof(GameCoinErrorCodes.EnvNotEnabled)] = "ENV_NOT_ENABLED",
            [nameof(GameCoinErrorCodes.NotFound)] = "NOT_FOUND",
            [nameof(GameCoinErrorCodes.InsufficientFunds)] = "INSUFFICIENT_FUNDS",
            [nameof(GameCoinErrorCodes.LimitReached)] = "LIMIT_REACHED",
            [nameof(GameCoinErrorCodes.IdempotencyConflict)] = "IDEMPOTENCY_CONFLICT",
            [nameof(GameCoinErrorCodes.Conflict)] = "CONFLICT",
            [nameof(GameCoinErrorCodes.ReadOnly)] = "READ_ONLY",
            [nameof(GameCoinErrorCodes.RateLimited)] = "RATE_LIMITED",
            [nameof(GameCoinErrorCodes.Internal)] = "INTERNAL",
            [nameof(GameCoinErrorCodes.NetworkError)] = "NETWORK_ERROR",
            [nameof(GameCoinErrorCodes.Timeout)] = "TIMEOUT",
            [nameof(GameCoinErrorCodes.InvalidResponse)] = "INVALID_RESPONSE",
        };

        var constants = typeof(GameCoinErrorCodes).GetFields().Where(f => f.IsLiteral).ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);
        Assert.Equal(expected, constants);
    }
}
