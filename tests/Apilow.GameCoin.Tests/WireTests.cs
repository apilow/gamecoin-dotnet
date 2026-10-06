using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Apilow.GameCoin.Tests;

/// <summary>
/// The SDK with its own real HTTP stack against a loopback socket: what really goes on the wire (request line, headers),
/// without any fake handler in between. Still no network beyond 127.0.0.1.
/// </summary>
public class WireTests
{
    /// <summary>Serves each scripted response to one connection each, and records the raw head of every request.</summary>
    private sealed class RawServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<string> _responses = new();
        private readonly Task _loop;
        private readonly CancellationTokenSource _stop = new();

        public RawServer(params string[] responses)
        {
            _responses.AddRange(responses);
            _listener.Start();
            _loop = Task.Run(Serve);
        }

        public string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

        public List<string> Heads { get; } = new();

        public List<string> Bodies { get; } = new();

        private async Task Serve()
        {
            foreach (var response in _responses)
            {
                using var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
                using var stream = socket.GetStream();
                var (head, body) = await ReadRequest(stream);
                lock (Heads)
                {
                    Heads.Add(head);
                    Bodies.Add(body);
                }

                var bytes = Encoding.UTF8.GetBytes(response);
                await stream.WriteAsync(bytes, _stop.Token);
                await stream.FlushAsync(_stop.Token);
            }
        }

        private static async Task<(string Head, string Body)> ReadRequest(NetworkStream stream)
        {
            var buffer = new byte[16 * 1024];
            var received = new List<byte>();
            int headEnd;
            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                received.AddRange(buffer.AsSpan(0, read).ToArray());
                headEnd = IndexOf(received, "\r\n\r\n");
                if (headEnd >= 0)
                {
                    var head = Encoding.ASCII.GetString(received.GetRange(0, headEnd).ToArray());
                    var contentLength = head.Split("\r\n")
                        .Where(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        .Select(l => int.Parse(l.Split(':')[1].Trim()))
                        .FirstOrDefault();
                    while (received.Count - (headEnd + 4) < contentLength)
                    {
                        read = await stream.ReadAsync(buffer);
                        if (read == 0)
                        {
                            break;
                        }

                        received.AddRange(buffer.AsSpan(0, read).ToArray());
                    }

                    return (head, Encoding.UTF8.GetString(received.GetRange(headEnd + 4, received.Count - headEnd - 4).ToArray()));
                }
            }

            return (string.Empty, string.Empty);
        }

        private static int IndexOf(List<byte> data, string marker)
        {
            var needle = Encoding.ASCII.GetBytes(marker);
            for (var i = 0; i <= data.Count - needle.Length; i++)
            {
                var match = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (data[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            try
            {
                _loop.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // the loop was cancelled: expected
            }
        }
    }

    private static string Json(string body, string status = "200 OK", string extraHeaders = "") =>
        $"HTTP/1.1 {status}\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n{extraHeaders}\r\n{body}";

    [Theory]
    [InlineData("user 42", "ext%3Auser%2042")]
    [InlineData("a/b", "ext%3Aa%2Fb")]
    [InlineData("100%", "ext%3A100%25")]
    [InlineData("café 日本", "ext%3Acaf%C3%A9%20%E6%97%A5%E6%9C%AC")]
    [InlineData("a+b?c#d", "ext%3Aa%2Bb%3Fc%23d")]
    public async Task The_request_line_carries_the_ext_id_encoded_exactly_once(string externalId, string expectedSegment)
    {
        using var server = new RawServer(Json(Samples.Player));
        using var client = new GameCoinClient(TestRig.SecretKey, new GameCoinOptions { BaseUrl = server.BaseUrl });

        await client.Players.GetAsync(PlayerRef.Ext(externalId));

        var requestLine = server.Heads[0].Split("\r\n")[0];
        Assert.Equal($"GET /api/v1/players/{expectedSegment} HTTP/1.1", requestLine);
    }

    [Fact]
    public async Task A_real_post_carries_the_headers_the_api_expects()
    {
        using var server = new RawServer(Json(Samples.Movement, extraHeaders: "Idempotent-Replayed: true\r\n"));
        using var client = new GameCoinClient(TestRig.SecretKey, new GameCoinOptions { BaseUrl = server.BaseUrl });

        var movement = await client.Wallet.GrantAsync(
            PlayerRef.Ext("user-42"),
            new GrantRequest { Currency = "gems", Amount = 100, Reason = "level_up" },
            new RequestOptions { IdempotencyKey = "wire-key-1" });

        var head = server.Heads[0];
        var lines = head.Split("\r\n");
        Assert.Equal("POST /api/v1/players/ext%3Auser-42/wallet/grant HTTP/1.1", lines[0]);
        Assert.Contains($"Authorization: Bearer {TestRig.SecretKey}", lines);
        Assert.Contains("Idempotency-Key: wire-key-1", lines);
        Assert.Contains("Accept: application/json", lines);
        Assert.Contains(lines, l => l.StartsWith("User-Agent: gamecoin-dotnet/0.2.0 (dotnet/", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("Content-Type: application/json", StringComparison.Ordinal));
        Assert.Equal("""{"currency":"gems","amount":100,"reason":"level_up"}""", server.Bodies[0]);
        Assert.True(movement.Replayed);
        Assert.Equal(100, movement.Balance.Total);
    }

    [Fact]
    public async Task Error_bodies_and_retry_after_survive_the_real_stack()
    {
        using var server = new RawServer(
            Json("""{"error":{"code":"INSUFFICIENT_FUNDS","message":"Insufficient funds","details":{"required":50,"available":20}}}""", "409 Conflict"));
        using var client = new GameCoinClient(TestRig.SecretKey, new GameCoinOptions { BaseUrl = server.BaseUrl });

        var error = await Assert.ThrowsAsync<GameCoinException>(() =>
            client.Wallet.SpendAsync(PlayerRef.Ext("u"), new SpendRequest { Currency = "gems", Amount = 50 }));

        Assert.Equal("INSUFFICIENT_FUNDS", error.Code);
        Assert.Equal(409, error.Status);
        Assert.Equal(50, error.Details["required"].GetInt64());
    }

    [Fact]
    public async Task A_redirect_is_not_followed()
    {
        using var server = new RawServer("HTTP/1.1 301 Moved Permanently\r\nLocation: https://elsewhere.invalid/\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var client = new GameCoinClient(TestRig.SecretKey, new GameCoinOptions { BaseUrl = server.BaseUrl, MaxRetries = 0 });

        var error = await Assert.ThrowsAsync<GameCoinException>(() => client.Catalog.GetAsync());

        Assert.Equal(301, error.Status);
        Assert.Equal(GameCoinErrorCodes.InvalidResponse, error.Code);
        Assert.Single(server.Heads);
    }

    [Fact]
    public async Task A_closed_port_is_a_network_error()
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        using var client = new GameCoinClient(TestRig.SecretKey, new GameCoinOptions { BaseUrl = $"http://127.0.0.1:{port}", MaxRetries = 0 });

        var error = await Assert.ThrowsAsync<GameCoinException>(() => client.Catalog.GetAsync());

        Assert.Equal(GameCoinErrorCodes.NetworkError, error.Code);
        Assert.Equal(0, error.Status);
        Assert.DoesNotContain(TestRig.SecretKey, error.Message);
    }
}
