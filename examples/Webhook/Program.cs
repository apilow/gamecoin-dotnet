using System.Collections.Concurrent;
using Apilow.GameCoin;

// A minimal ASP.NET endpoint that receives GameCoin webhooks.
// GAMECOIN_WEBHOOK_SECRET holds the signing secret (whsec_gc_...) the dashboard shows when you create the endpoint.
var secret = Environment.GetEnvironmentVariable("GAMECOIN_WEBHOOK_SECRET")
    ?? throw new InvalidOperationException("Set GAMECOIN_WEBHOOK_SECRET to the signing secret of your endpoint.");

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var processed = new ConcurrentDictionary<string, bool>(); // in real code: a database table keyed by the event id

app.MapPost("/gamecoin/webhook", async (HttpRequest request) =>
{
    // The RAW body, exactly as received: never a parsed and serialized object, or the signature fails.
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);

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
        return Results.Ok(); // GameCoin can send an event more than once: nothing to do the second time
    }

    switch (webhookEvent.Data)
    {
        case Order order when webhookEvent.Type == WebhookEventTypes.OrderFulfilled:
            app.Logger.LogInformation("Order {Order} delivered: unlock {Pack} for {Player}", order.Id, order.Pack.Sku, order.PlayerId);
            break;
        case Order order:
            app.Logger.LogInformation("Order {Order} is now {Status}", order.Id, order.Status);
            break;
        case WalletCreditedData credited:
            app.Logger.LogInformation("{Player} was credited {Credits}", credited.PlayerId, credited.Credits.Count);
            break;
        default:
            app.Logger.LogInformation("Event {Type}", webhookEvent.Type); // webhook.test, or a type added later
            break;
    }

    return Results.Ok(); // answer fast (a 2xx within 10 seconds); do heavy work in the background
});

app.Run();
