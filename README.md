# Apilow.GameCoin for .NET

Server SDK for **Apilow GameCoin**, the bank of your game: virtual currencies, inventory and sales, behind one API.
Use it from your game backend (ASP.NET, a worker, a dedicated server) to grant and spend coins, manage items, and sell
packs with real money. Full documentation: [gamecoin.apilow.com/docs/sdk/dotnet](https://gamecoin.apilow.com/docs/sdk/dotnet).

- `net8.0` and `netstandard2.1` (so Unity editor tools and dedicated servers can use it too).
- **No dependency** on `net8.0` (`HttpClient`, `System.Text.Json`). On `netstandard2.1` the only dependency is `System.Text.Json`.
- Everything is `async`, takes a `CancellationToken`, and returns immutable `record`s.
- Safe retries built in: every call that changes something carries an idempotency key.

> This is a **server** SDK. It uses your **secret key** (`gc_sk_test_…` / `gc_sk_live_…`), which must never be shipped
> in a game client or a web page. For the browser, use the GameCoin browser SDK with the publishable key.

## Install

```sh
dotnet add package Apilow.GameCoin
```

## Quickstart

> The snippets assume [implicit usings](https://learn.microsoft.com/dotnet/core/project-sdk/overview#implicit-using-directives)
> (`System`, `System.Linq`, `System.Threading.Tasks`…), which new .NET projects enable by default.

Grant gems to a player, spend some, and start the purchase of a pack (`GAMECOIN_SECRET_KEY` holds a `gc_sk_test_…`
key; `GAMECOIN_BASE_URL` is optional and defaults to production):

```csharp
using Apilow.GameCoin;

using var gamecoin = new GameCoinClient(Environment.GetEnvironmentVariable("GAMECOIN_SECRET_KEY")!,
    new GameCoinOptions { BaseUrl = Environment.GetEnvironmentVariable("GAMECOIN_BASE_URL") ?? GameCoinClient.DefaultBaseUrl });
var player = PlayerRef.Ext($"quickstart-{Guid.NewGuid():N}"); // your own id for the player (a fresh one: every run starts at zero)

var granted = await gamecoin.Wallet.GrantAsync(player, new GrantRequest { Currency = "gems", Amount = 100, Reason = "level_up" });
var spent = await gamecoin.Wallet.SpendAsync(player, new SpendRequest { Currency = "gems", Amount = 30 });
var checkout = await gamecoin.Checkouts.CreateAsync(player, new CheckoutRequest { PackSku = "gems-500" });
Console.WriteLine($"gems: {granted.Balance.Total} -> {spent.Balance.Total}; the player pays at {checkout.CheckoutUrl}");
```

This prints `gems: 100 -> 70; the player pays at https://…/pay/<order id>?t=…`. The same program lives in
`examples/Quickstart`. The other examples below continue from this client.

## Configuration

```csharp
using Apilow.GameCoin;

using var gamecoin = new GameCoinClient(
    Environment.GetEnvironmentVariable("GAMECOIN_SECRET_KEY")!,
    new GameCoinOptions
    {
        BaseUrl = Environment.GetEnvironmentVariable("GAMECOIN_BASE_URL") ?? "https://gamecoin.apilow.com", // the default
        Timeout = TimeSpan.FromSeconds(30), // per HTTP attempt; the default
        MaxRetries = 2,                     // automatic retries; the default (three attempts). 0 turns them off
    });

Console.WriteLine($"{gamecoin.Environment} environment at {gamecoin.BaseUrl}"); // "test" or "live", read from the key
```

| Option | Default | Meaning |
|---|---|---|
| `BaseUrl` | `https://gamecoin.apilow.com` | Origin of the service; the SDK appends `/api/v1`, drops trailing `/` and keeps a path prefix. Only `http(s)` without credentials, query or fragment. |
| `Timeout` | 30 s | Timeout of each HTTP attempt (a retry gets a fresh one). |
| `MaxRetries` | 2 | Automatic retries, 0 to 10. See [Idempotency and retries](#idempotency-and-retries). |
| `HttpClient` | created for you | Your own `HttpClient`, for instance from `IHttpClientFactory`. Its default headers are never touched and it is never disposed. |
| `HttpMessageHandler` | none | A handler the SDK wraps in its own `HttpClient` (tests, proxies, logging). Not disposed either. |

`HttpClient` and `HttpMessageHandler` are mutually exclusive. The constructor rejects anything that is not a secret key
(surrounding white space, such as a newline read from a file, is trimmed); a publishable key (`gc_pk_…`) gets a message that
points to the browser SDK. The key never appears in an exception, and `ToString()` of the client shows its prefix only:
`gc_sk_test_…`.

Create **one** client per application and keep it (register it as a singleton): it is thread-safe and reuses its
connections. `GameCoinClient` is `IDisposable` and disposes only the `HttpClient` it created itself.

Every request carries `Authorization: Bearer <key>`, `Accept: application/json` and
`User-Agent: gamecoin-dotnet/<version> (dotnet/<runtime version>)`.

## Players and the `ext` helper

A player is a GameCoin player id, or your game's own id through `PlayerRef.Ext(...)` (sent as `ext:<your id>`):

```csharp
var player = PlayerRef.Ext("user-42"); // your game's id; any string works: spaces, slashes and accents are encoded for you
var created = await gamecoin.Players.UpsertAsync(player, new PlayerProfile { DisplayName = "Ann", Country = "BE" });
Console.WriteLine($"{created.Id} is {created.ExternalId} ({created.DisplayName}, {created.Country})");

var again = await gamecoin.Players.GetAsync(PlayerRef.FromId(created.Id)); // by GameCoin id
var token = await gamecoin.Players.CreateTokenAsync(player);               // short-lived, for the browser SDK
Console.WriteLine($"{again.Kind}, token valid until {token.ExpiresAt:u}");

await gamecoin.Players.UpsertAsync(player, new PlayerProfile { Clear = PlayerProfileFields.Country }); // erase a field
```

- A write call (`UpsertAsync`, grant, spend, purchase…) on an unknown `ext:` player creates it. A read (`GetAsync`,
  `Wallet.GetAsync`…) answers `NOT_FOUND`.
- In a `PlayerProfile`, a `null` property leaves the field unchanged; list a field in `Clear` to erase it.
- Path segments are URL-encoded **exactly once**. A plain `string` converts to a GameCoin id: `GetAsync("6ac4…")`.

## Wallet and ledger

```csharp
var wallet = await gamecoin.Wallet.GetAsync(player); // one line per currency, zeros included
foreach (var balance in wallet.Balances)
{
    Console.WriteLine($"{balance.Currency}: {balance.Total} ({balance.Paid} paid + {balance.Bonus} bonus)");
}

var metadata = new Dictionary<string, string> { ["level"] = "7" };
var grant = await gamecoin.Wallet.GrantAsync(player, new GrantRequest { Currency = "gems", Amount = 100, Reason = "level_up", Metadata = metadata });
var spend = await gamecoin.Wallet.SpendAsync(player, new SpendRequest { Currency = "gems", Amount = 30, Reason = "shop" });
Console.WriteLine($"{grant.Entry.Type} then {spend.Entry.Type}: balance {spend.Balance.Total}");

await foreach (var entry in gamecoin.Ledger.IterateAsync(player, new LedgerQuery { Currency = "gems", Limit = 50 }))
{
    Console.WriteLine($"{entry.CreatedAt:u} {entry.Type} {entry.BonusDelta + entry.PaidDelta} -> {entry.BalanceAfter.Total}");
}

LedgerPage page = await gamecoin.Ledger.ListAsync(player, new LedgerQuery { Limit = 20 }); // one page, newest first
Console.WriteLine($"{page.Entries.Count} entries, next cursor: {page.NextCursor ?? "none"}");
```

- A grant always credits the *bonus* bucket. Amounts are `long` and must be positive.
- `Ledger.IterateAsync` is an `IAsyncEnumerable<LedgerEntry>`: pages are fetched lazily, one at a time, as you read.
  `LedgerQuery.Cursor` resumes from a given page; `Limit` is the page size (1 to 100, default 20).
- Spending more than the player has raises `INSUFFICIENT_FUNDS` (see [Errors](#errors)).

## Inventory and purchases

```csharp
var catalog = await gamecoin.Catalog.GetAsync(); // active currencies, items and packs
Console.WriteLine(string.Join(", ", catalog.Items.Select(i => $"{i.Sku} ({i.Type}, {i.Price?.Amount} {i.Price?.Currency})")));

var given = await gamecoin.Inventory.GrantAsync(player, new InventoryGrantRequest { Sku = "potion", Quantity = 2 }); // for free
var used = await gamecoin.Inventory.ConsumeAsync(player, new InventoryConsumeRequest { Sku = "potion" });           // quantity defaults to 1
Console.WriteLine($"{given.Quantity} then {used.Quantity} potion(s)");

var bought = await gamecoin.Purchases.CreateAsync(player, new PurchaseRequest { Sku = "potion" }); // pays the item's price in its currency
Console.WriteLine($"bought: {bought.Item.Quantity} potion(s), {bought.Balance.Total} {bought.Balance.Currency} left");

foreach (var item in await gamecoin.Inventory.ListAsync(player)) // owned items only (quantity above 0)
{
    Console.WriteLine($"{item.Sku} x{item.Quantity}");
}
```

`Inventory.ConsumeAsync` returns the item even when it reaches 0. A durable item with `MaxOwned` raises `LIMIT_REACHED`
beyond the limit.

The catalog lists every active pack, and a pack that is not an ordinary one carries an `Availability` (`null` for an
ordinary pack, always on sale): its sale window (`StartsAt`, included, and `EndsAt`, excluded, both `DateTimeOffset?`), the
stock left (`Remaining`, `null` when unlimited, never below 0, counted in the environment of your key) and whether it is a
founder pack (`Founder`). Scheduled, ended and sold-out packs are in the list too: your shop decides what to show.
`Catalog.Prelaunch` and `Catalog.LaunchAt` tell a shop to show pre-orders with the launch date.

```csharp
foreach (var pack in catalog.Packs)
{
    var availability = pack.Availability; // null: an ordinary pack
    var stock = availability?.Remaining is { } left ? $"{left} left" : "no limit";
    Console.WriteLine($"{pack.Sku}: {(availability?.Founder == true ? "founder, " : "")}{stock}, on sale from {availability?.StartsAt?.ToString("u") ?? "always"}");
}

if (catalog.Prelaunch)
{
    Console.WriteLine($"pre-order, launch on {catalog.LaunchAt:u}");
}
```

## Manage the catalog

`Currencies`, `Items` and `Packs` create, change and archive the catalog, the way a payment platform manages its products and
prices. Each has `CreateAsync`, `GetAsync`, `UpdateAsync`, `ArchiveAsync` and `ListAsync`.

> Each environment has its own catalog: the key decides which one you change, and a pack created with a test key exists in test only. Create a new pack as a draft (the default) and turn it on with `Status = PackStatus.Active` when it is ready.

```csharp
await gamecoin.Currencies.CreateAsync(new CreateCurrencyRequest { Code = "rubies", Name = "Rubies" });
await gamecoin.Items.CreateAsync(new CreateItemRequest
{
    Sku = "elixir", Name = "Elixir", Type = ItemType.Consumable, Price = new ItemPrice { Currency = "gems", Amount = 15 },
});

var pack = await gamecoin.Packs.CreateAsync(new CreatePackRequest
{
    Sku = "mega",
    Name = "Mega pack",
    PriceCents = 999,
    Grants = new[] { new PackGrant { Currency = "gems", Amount = 1200, Bonus = 100 } },
    Items = new[] { new PackItem { Sku = "elixir", Quantity = 2 } },
});
Console.WriteLine(pack.Status); // draft

await gamecoin.Packs.UpdateAsync("mega", new UpdatePackRequest
{
    PriceCents = 1099,
    Availability = new PackAvailabilityRequest { StartsAt = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), Stock = 500, Founder = true }, // replaces the whole availability
    Status = PackStatus.Active,
});
await gamecoin.Items.UpdateAsync("elixir", new UpdateItemRequest { Clear = ItemFields.Price }); // erase the price
await gamecoin.Packs.ArchiveAsync("mega"); // nothing is deleted
var everything = await gamecoin.Packs.ListAsync(includeArchived: true);
```

A `null` property of an `Update` request is left unchanged; to erase a description, a price, a badge or a limit, list it in
`Clear`. The identifiers (`Code`, `Sku`) and the `Type` of an item are final, so the update requests have no property for them.
A taken identifier is `Conflict` with `Details["fieldErrors"]` holding `"sku": ["SKU_TAKEN"]` (`"code": ["CODE_TAKEN"]` for a
currency); an invalid pack is `ValidationFailed` with the faulty lines in `Details["fieldErrors"]`; archiving something that an
active pack still uses is `Conflict` with `Details["blockedBy"]`. A create has no idempotency key (the identifier is its natural
key), so it is retried on `429` only: after a network error, a timeout or a `5xx`, read the resource back with `GetAsync`
before you try again.

## Checkout, orders and refunds

A **checkout** sells a *pack* for real money: you create an order, send the player to `CheckoutUrl`, and the coins and
items are delivered when the payment succeeds.

```csharp
var checkout = await gamecoin.Checkouts.CreateAsync(player, new CheckoutRequest
{
    PackSku = "gems-500",
    SuccessUrl = "https://game.example.com/shop/thanks", // optional: where the player lands after paying
    CancelUrl = "https://game.example.com/shop",         // optional
    Country = "BE",                                      // optional: the VAT country (server side only)
    Locale = CheckoutLocale.English,                     // optional: "fr", "en" or "nl", the language of the payment page, receipt and e-mails
    CreatorCode = "STREAMER10",                          // optional: a creator code the player typed (sent as is, not sent when null)
});
Console.WriteLine($"{checkout.Order.Status}: {checkout.Order.AmountCents} cents ({checkout.Order.Currency}), pay at {checkout.CheckoutUrl}");

var order = await gamecoin.Orders.GetAsync(checkout.Order.Id); // poll, or call it when the player comes back
Console.WriteLine($"{order.Id}: {order.Status}, paid at {order.PaidAt?.ToString("u") ?? "(not yet)"}");

try
{
    await gamecoin.Orders.RefundAsync(order.Id, new RefundRequest { Reason = "customer asked" }); // takes the coins and items back
}
catch (GameCoinException ex) when (ex.Code == GameCoinErrorCodes.Conflict)
{
    Console.WriteLine($"cannot refund an unpaid order: {ex.Message}");
}
```

In test mode the payment is simulated: open `CheckoutUrl` and press "Pay (simulated)". With a live key the page takes a real payment.

`Locale` is the language of the hosted payment page, of the receipt and of the e-mails of the order. The API ignores any
value other than `fr`, `en` and `nl` without an error, and the SDK does not send the field when it is `null`. A pending
order that the API reuses takes the language of the latest request.

A **creator code** gives the buyer a bonus of free coins on delivery and the creator a commission (both are cancelled on
refund). `Order.CreatorCode` (`null` when there is none) shows `Code` and `CreatorName`, never the rates, and it travels in the
`order.*` webhooks. A code the API refuses (unknown, disabled, not valid for the pack…) raises `VALIDATION_FAILED` with
`fieldErrors.creatorCode = ["CODE_INVALID"]`:

```csharp
try
{
    await gamecoin.Checkouts.CreateAsync(player, new CheckoutRequest { PackSku = "gems-500", CreatorCode = "NOT-A-CODE" });
}
catch (GameCoinException ex) when (ex.Code == GameCoinErrorCodes.ValidationFailed
                                   && ex.Details["fieldErrors"].TryGetProperty("creatorCode", out _))
{
    Console.WriteLine("this creator code is not valid"); // let the player try another one, or buy without a code
}
catch (GameCoinException ex) when (ex.Code == GameCoinErrorCodes.PackNotAvailable)
{
    Console.WriteLine($"not on sale: from {ex.Details["startsAt"]} to {ex.Details["endsAt"]}"); // ISO 8601 text, or null
}
catch (GameCoinException ex) when (ex.Code == GameCoinErrorCodes.PackSoldOut)
{
    Console.WriteLine($"sold out: {ex.Details["packSku"]}");
}
```

A pack outside its sale window is `PACK_NOT_AVAILABLE` (`Details["startsAt"]` and `Details["endsAt"]`, ISO 8601 text or
`null`) and a pack without a unit left is `PACK_SOLD_OUT` (`Details["packSku"]`), both HTTP 409 and never retried. The window
is checked when the order is created: an order created in time stays payable.

**Refunds have no idempotency key**, so the SDK retries `RefundAsync` on `429` only. After a network error, a timeout or
a `5xx` it raises, and you should check the outcome with `Orders.GetAsync` (status `refunded` or not) before trying again.

## Codes

A **gift code** is a code the studio issued (limited uses, one use per player, optional expiry): the player types it, and
you redeem it for them. It adds free coins (the *bonus* bucket, never paid coins) and items.

```csharp
try
{
    Redemption redemption = await gamecoin.Codes.RedeemAsync(player, "WELCOME-2026"); // idempotent, like a grant
    foreach (var grant in redemption.Grants)
    {
        Console.WriteLine($"+{grant.Amount} {grant.Currency} -> {grant.Balance.Total}");
    }

    Console.WriteLine($"items: {string.Join(", ", redemption.Items.Select(i => $"{i.Sku} x{i.Quantity}"))}");
    Console.WriteLine($"skipped: {string.Join(", ", redemption.Skipped.Select(i => $"{i.Sku} x{i.Quantity}"))}"); // items the player could not receive
}
catch (GameCoinException ex) when (ex.Code == GameCoinErrorCodes.ValidationFailed)
{
    Console.WriteLine("this code is not valid"); // fieldErrors.code = ["CODE_INVALID"]
}
```

- Send the code as the player typed it: the API ignores case, spaces and dashes, and the SDK does not change it. It only
  checks that it is not empty.
- Every refusal of the code itself (unknown, malformed, expired, used up, disabled, already used by this player, or a creator
  code) is the same `VALIDATION_FAILED` with `fieldErrors.code = ["CODE_INVALID"]`: nothing tells a guess from a real code, and
  the SDK never retries it. Attempts are rate limited: `RATE_LIMITED` carries `RetryAfter`.
- A blocked player is `PLAYER_BLOCKED`. A code whose content the player can no longer receive at all is `LIMIT_REACHED` (they
  own the maximum of every item) or `CONFLICT` (a currency or an item was archived since); the code is not used up then.
- Like every call that changes something, `RedeemAsync` carries an idempotency key (generated once per call, or yours in
  `RequestOptions`): replaying the same key returns the same `Redemption` with `Replayed == true` and credits nothing twice.
- A **creator code** is a different thing: it goes in `CheckoutRequest.CreatorCode`, see
  [Checkout, orders and refunds](#checkout-orders-and-refunds).

## Webhooks

GameCoin calls **your server** when an order is delivered, refunded or disputed (declare the endpoint and get its signing
secret `whsec_gc_…` in the dashboard). `Webhooks.ConstructEvent` checks the `GameCoin-Signature` header and returns the typed
event. It is a pure function: it needs no client and makes no request.

> [!WARNING]
> Verify the signature on the **raw body**, exactly as received, before any JSON parsing. A body that was parsed and
> serialized again can differ by one character and the signature then fails. In ASP.NET Core, do not bind the body to a
> model on this route: copy `Request.Body` to a `MemoryStream` as below.

A complete ASP.NET minimal API endpoint (also in `examples/Webhook`):

```csharp
using System.Collections.Concurrent;
using Apilow.GameCoin;

var secret = Environment.GetEnvironmentVariable("GAMECOIN_WEBHOOK_SECRET")!; // whsec_gc_…, from the dashboard
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var processed = new ConcurrentDictionary<string, bool>(); // in real code: a database table keyed by the event id

app.MapPost("/gamecoin/webhook", async (HttpRequest request) =>
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer); // the raw body, untouched

    WebhookEvent webhookEvent;
    try
    {
        webhookEvent = Webhooks.ConstructEvent(buffer.ToArray(), request.Headers["GameCoin-Signature"].ToString(), secret);
    }
    catch (WebhookSignatureException ex)
    {
        app.Logger.LogWarning("Webhook refused: {Reason}", ex.Reason);
        return Results.BadRequest();
    }

    if (!processed.TryAdd(webhookEvent.Id, true))
    {
        return Results.Ok(); // GameCoin can send an event more than once: process it once
    }

    switch (webhookEvent.Data)
    {
        case Order order when webhookEvent.Type == WebhookEventTypes.OrderFulfilled:
            Console.WriteLine($"unlock {order.Pack.Sku} for {order.PlayerId}");
            break;
        case WalletCreditedData credited:
            Console.WriteLine($"{credited.PlayerId} received {credited.Credits.Count} credit(s)");
            break;
    }

    return Results.Ok(); // a 2xx within 10 seconds
});

app.Run();
```

`ConstructEvent(rawBody, signatureHeader, secret, options)` takes the raw body as a `string` or a `byte[]` (never a parsed
object), the value of the `GameCoin-Signature` header (`t=<unix seconds>,v1=<hex>`), and the signing secret (trimmed;
never put it in a log). Every `v1` of the header is compared in constant time and unknown keys are ignored. `WebhookOptions`
has `ToleranceSeconds` (default 300; `0` turns the timestamp check off) and `Now` (a clock, for tests).

The returned `WebhookEvent` has `Id`, `Type`, `CreatedAt`, `Env` and `Data`. `Type` is one of the `WebhookEventTypes`
constants, and `Data` is typed by it:

| `Type` | `Data` |
|---|---|
| `order.fulfilled`, `order.refunded`, `order.chargeback` | `Order`, the same object as `Orders.GetAsync` (with its `CreatorCode`, or `null`) |
| `wallet.credited` | `WalletCreditedData` (`PlayerId`, `OrderId`, `Credits` with `Currency`, `Paid`, `Bonus`) |
| `webhook.test` | `WebhookTestData` (`Message`) |
| anything else | the untouched `JsonElement`: an event type added later never fails |

A call that cannot be trusted raises `WebhookSignatureException` (it does **not** derive from `GameCoinException`: no API
call is involved). Answer `400`. Its `Reason` is one of the `WebhookSignatureReason` constants: `missing_header`,
`malformed_header`, `no_matching_signature` (wrong secret, altered body, or a body that was parsed and re-serialized),
`timestamp_out_of_tolerance` (a replayed call or a skewed clock) and `invalid_payload` (the signature is right but the body
is not an event). The message never contains the secret, the body or the signature. A missing secret or body is an
`ArgumentException` instead.

An event is delivered **at least once**: keep the ids you have processed (`Id` is stable for an event). Answer `2xx` quickly,
then do the heavy work.

## Idempotency and retries

Every call that changes a wallet or an inventory, or creates a checkout, sends an `Idempotency-Key` header. If you give
none, the SDK generates a UUID v4 **once per call** and reuses it for each automatic retry, so a retry can never credit
twice. To make *your own* retries safe (a job that may run twice), supply a stable key:

```csharp
var key = new RequestOptions { IdempotencyKey = $"quest-completed-{Guid.NewGuid():N}" }; // in real code: derive it from the event, e.g. "quest-completed-<player>-<quest>"
var request = new GrantRequest { Currency = "gold", Amount = 25, Reason = "quest" };

var first = await gamecoin.Wallet.GrantAsync(player, request, key);
var second = await gamecoin.Wallet.GrantAsync(player, request, key); // same key, same request: the original response
Console.WriteLine($"{first.Replayed}, {second.Replayed}, same entry: {first.Entry.Id == second.Entry.Id}"); // False, True, same entry: True
```

`Replayed` is `true` when the API answered with the original response of an earlier request with the same key (header
`Idempotent-Replayed: true`); it exists on `Movement`, `Purchase`, `Checkout`, `Redemption` and `InventoryItem`. The same key with a
*different* request raises `IDEMPOTENCY_CONFLICT`. A key is 1 to 100 printable ASCII characters and is kept for 30 days.
A replayed checkout returns the order in its **current** state.

Automatic retries (at most `MaxRetries`, so three attempts by default):

| Situation | Retried? |
|---|---|
| Network error, timeout, `5xx`, `429` | yes |
| Any other `4xx` | never |
| `RefundAsync` | on `429` only |

Every read, `UpsertAsync`, `CreateTokenAsync` and every call with an idempotency key (including `Codes.RedeemAsync`) follows the first line; a call cancelled
through its `CancellationToken` is never retried (`OperationCanceledException`).

The delay is `0.5 s × 2^attempt × a random factor in [0.75, 1.25]`. A `Retry-After` header (seconds or an HTTP date) wins,
up to 30 s inclusive, on a `429` as on a `5xx`; beyond that the SDK does not wait: it raises the error with `RetryAfter` set
so that you decide. A malformed `200` (`INVALID_RESPONSE`) is never retried; an HTML `502` from a proxy is retried like any
`5xx`.

## Errors

Every failed call raises `GameCoinException` (arguments rejected locally raise the usual `ArgumentException` before any
request, and a cancelled call raises `OperationCanceledException`):

| Property | Content |
|---|---|
| `Code` | API code (`INSUFFICIENT_FUNDS`, `NOT_FOUND`…), or `NETWORK_ERROR` / `TIMEOUT` (status 0), or `INVALID_RESPONSE` (the body is not the expected JSON, or a field has the wrong shape: the HTTP status is kept) |
| `Status` | HTTP status, 0 when no response was received |
| `Message` | Message of the API, or a short description |
| `Details` | `details` of the API as `IReadOnlyDictionary<string, JsonElement>` (`required`, `available`, `fieldErrors`…), empty when absent |
| `RetryAfter` | `TimeSpan?` (the header is in seconds), set when the API sent `Retry-After` |

```csharp
try
{
    await gamecoin.Wallet.SpendAsync(player, new SpendRequest { Currency = "gems", Amount = 1_000_000 });
}
catch (GameCoinException ex) when (ex.Code == GameCoinErrorCodes.InsufficientFunds)
{
    Console.WriteLine($"needs {ex.Details["required"].GetInt64()}, has {ex.Details["available"].GetInt64()}");
}
catch (GameCoinException ex) when (ex.Code == GameCoinErrorCodes.RateLimited)
{
    Console.WriteLine($"slow down, retry in {ex.RetryAfter}");
}
catch (GameCoinException ex)
{
    Console.WriteLine($"{ex.Code} (HTTP {ex.Status}): {ex.Message}");
}
```

The codes are constants of `GameCoinErrorCodes`: `ValidationFailed`, `IdempotencyKeyRequired`, `Unauthenticated`,
`PaymentFailed`, `Forbidden`, `PlayerBlocked`, `EnvNotEnabled`, `NotFound`, `InsufficientFunds`, `LimitReached`,
`PackNotAvailable`, `PackSoldOut`, `IdempotencyConflict`, `Conflict`, `ReadOnly`, `RateLimited`, `Internal`, plus the SDK's own `NetworkError`, `Timeout`
and `InvalidResponse`. `Details["fieldErrors"]` is meant for developers, not for parsing. Unknown fields in a response are
ignored, an API code this version does not know is passed through as is, and an unknown future `Status` or `Type` value
stays readable as a string.

**Arguments are checked before any request**: an empty player, currency, sku or order id (or an empty optional string such
as `SuccessUrl`), an `Amount` or `Quantity` below 1, a `Limit` outside 1 to 100, an invalid idempotency key raise
`ArgumentException` (`ArgumentOutOfRangeException` for numbers), not a `GameCoinException`; the message names the argument,
never its value.

## Running the tests

```sh
dotnet test                                                # unit tests, no network (net8.0 library)
dotnet test tests/Apilow.GameCoin.Tests -p:LibraryTarget=netstandard2.1   # the same tests against the netstandard2.1 build
```

The unit tests use a fake `HttpMessageHandler` (and a loopback socket for the wire format). The **integration test** is
skipped unless `GAMECOIN_SECRET_KEY` and `GAMECOIN_BASE_URL` are set; it runs against the sandbox game created by
`npm run sdk:sandbox` in the GameCoin application (currencies `gems` and `gold`, items `potion` and `fire-sword`, packs
`starter` and `gems-500`):

```sh
GAMECOIN_SECRET_KEY=gc_sk_test_… GAMECOIN_BASE_URL=http://localhost:3400 dotnet test
```

## Status

Pre-release (`0.4.0`). The API has a **test environment** (simulated payments) and a **live environment** (real payments), reserved for verified studios. The
package and repository names may change before the first public release.

## Licence

MIT, Copyright (c) 2026 Apilow.
