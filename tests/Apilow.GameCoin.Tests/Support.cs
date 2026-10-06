using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Apilow.GameCoin.Tests;

/// <summary>What the fake handler saw of one request.</summary>
public sealed class RecordedRequest
{
    public required HttpMethod Method { get; init; }

    public required Uri Uri { get; init; }

    public required Dictionary<string, string> Headers { get; init; }

    public required string Body { get; init; }

    /// <summary>Path and query as they would go on the wire.</summary>
    public string PathAndQuery => Uri.PathAndQuery;

    public string Header(string name) => Headers.TryGetValue(name, out var value) ? value : string.Empty;

    public bool HasHeader(string name) => Headers.ContainsKey(name);

    public JsonElement Json() => JsonDocument.Parse(Body).RootElement;
}

/// <summary>A scripted <see cref="HttpMessageHandler"/>: answers requests in order and records them. No network.</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _script = new();

    public List<RecordedRequest> Requests { get; } = new();

    public bool Disposed { get; private set; }

    public FakeHandler Reply(HttpStatusCode status, string json, params (string Name, string Value)[] headers)
    {
        _script.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        });
        return this;
    }

    public FakeHandler Ok(string json, params (string Name, string Value)[] headers) => Reply(HttpStatusCode.OK, json, headers);

    public FakeHandler Error(HttpStatusCode status, string code, string message, string? detailsJson = null, params (string Name, string Value)[] headers)
    {
        var details = detailsJson is null ? string.Empty : $",\"details\":{detailsJson}";
        return Reply(status, $"{{\"error\":{{\"code\":\"{code}\",\"message\":\"{message}\"{details}}}}}", headers);
    }

    public FakeHandler Raw(HttpStatusCode status, string body, string contentType = "text/html", params (string Name, string Value)[] headers)
    {
        _script.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, contentType) };
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        });
        return this;
    }

    public FakeHandler Throw(Exception exception)
    {
        _script.Enqueue(() => throw exception);
        return this;
    }

    public FakeHandler Hang()
    {
        _script.Enqueue(() => throw new HangException());
        return this;
    }

    private sealed class HangException : Exception
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(header.Key == "User-Agent" ? " " : ", ", header.Value);
        }

        var body = string.Empty;
        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }

            body = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        Requests.Add(new RecordedRequest { Method = request.Method, Uri = request.RequestUri!, Headers = headers, Body = body });

        if (_script.Count == 0)
        {
            throw new InvalidOperationException("The fake handler has no scripted response left.");
        }

        var next = _script.Dequeue();
        try
        {
            return next();
        }
        catch (HangException)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        Disposed = true;
        base.Dispose(disposing);
    }
}

/// <summary>Builds clients that never really wait: delays are recorded, jitter is neutral.</summary>
public sealed class TestRig
{
    public const string SecretKey = "gc_sk_test_SECRETVALUE0123456789abcdef";

    public TestRig(int maxRetries = 2, TimeSpan? timeout = null, string baseUrl = "https://gc.example.test", double jitter = 0.5)
    {
        Handler = new FakeHandler();
        Client = new GameCoinClient(
            SecretKey,
            new GameCoinOptions
            {
                BaseUrl = baseUrl,
                MaxRetries = maxRetries,
                Timeout = timeout ?? TimeSpan.FromSeconds(30),
                HttpMessageHandler = Handler,
                DelayAsync = (delay, _) =>
                {
                    Delays.Add(delay);
                    return Task.CompletedTask;
                },
                Jitter = () => jitter, // 0.5 is neutral: factor 1.0, delays are exactly 0.5 s x 2^attempt
            });
    }

    public FakeHandler Handler { get; }

    public GameCoinClient Client { get; }

    public List<TimeSpan> Delays { get; } = new();

    public RecordedRequest Only => Assert.Single(Handler.Requests);
}

public static class Samples
{
    public const string Player =
        """{"id":"6ac40ac02e9fe50981a1e085","externalId":"probe-190","kind":"external","displayName":"Probe","country":null,"blocked":false,"createdAt":"2026-10-05T20:38:24.539Z","somethingNew":{"a":1}}""";

    public const string Entry =
        """{"id":"6ac40ac312fe8b4190d46ffc","type":"grant","currency":"gems","paidDelta":0,"bonusDelta":100,"balanceAfter":{"paid":0,"bonus":100,"total":100},"reason":"level_up","metadata":{"a":"b"},"ref":{},"createdAt":"2026-10-05T20:38:27.227Z"}""";

    public const string Balance = """{"currency":"gems","paid":0,"bonus":100,"total":100}""";

    public const string Movement = $$"""{"entry":{{Entry}},"balance":{{Balance}}}""";

    public const string Item = """{"sku":"potion","quantity":3,"updatedAt":"2026-10-05T20:40:00.000Z"}""";

    public const string Order =
        """{"id":"o1","playerId":"p1","status":"pending","pack":{"sku":"gems-500","name":"Bag of gems"},"amountCents":499,"currency":"EUR","country":"BE","vatRateBp":2100,"vatCents":87,"netCents":412,"createdAt":"2026-10-05T20:38:24.539Z","paidAt":null,"fulfilledAt":null,"refundedAt":null,"creatorCode":null}""";

    /// <summary>An order made with a creator code.</summary>
    public const string OrderWithCreator =
        """{"id":"o2","playerId":"p1","status":"fulfilled","pack":{"sku":"gems-500","name":"Bag of gems"},"amountCents":499,"currency":"EUR","country":"BE","vatRateBp":2100,"vatCents":87,"netCents":412,"createdAt":"2026-10-05T20:38:24.539Z","paidAt":"2026-10-05T20:39:00.000Z","fulfilledAt":"2026-10-05T20:39:01.000Z","refundedAt":null,"creatorCode":{"code":"STREAMER10","creatorName":"Ann the Streamer"}}""";

    public const string Redemption =
        """{"grants":[{"currency":"gems","amount":50,"balance":{"currency":"gems","paid":0,"bonus":150,"total":150}},{"currency":"gold","amount":10,"balance":{"currency":"gold","paid":0,"bonus":10,"total":10}}],"items":[{"sku":"potion","quantity":2}],"skipped":[{"sku":"fire-sword","quantity":1}],"somethingNew":true}""";

    public const string Catalog =
        """{"currencies":[{"code":"gems","name":"Gems","purchasable":true},{"code":"gold","name":"Gold","purchasable":false}],"items":[{"sku":"potion","name":"Potion","description":null,"type":"consumable","price":{"currency":"gems","amount":20},"maxOwned":null,"clientPurchasable":true,"metadata":{}},{"sku":"fire-sword","name":"Fire sword","description":"Hot","type":"durable","price":{"currency":"gems","amount":300},"maxOwned":1,"clientPurchasable":true,"metadata":{"tier":"3"}}],"packs":[{"sku":"starter","name":"Starter pack","description":null,"priceCents":99,"currency":"EUR","grants":[{"currency":"gems","amount":100,"bonus":20}],"items":[{"sku":"potion","quantity":1}],"badge":"new","maxPerPlayer":1,"availability":null}],"prelaunch":false,"launchAt":null}""";
}
