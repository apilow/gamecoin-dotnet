using Apilow.GameCoin;

using var gamecoin = new GameCoinClient(Environment.GetEnvironmentVariable("GAMECOIN_SECRET_KEY")!,
    new GameCoinOptions { BaseUrl = Environment.GetEnvironmentVariable("GAMECOIN_BASE_URL") ?? GameCoinClient.DefaultBaseUrl });
var player = PlayerRef.Ext($"quickstart-{Guid.NewGuid():N}"); // your own id for the player (a fresh one: every run starts at zero)

var granted = await gamecoin.Wallet.GrantAsync(player, new GrantRequest { Currency = "gems", Amount = 100, Reason = "level_up" });
var spent = await gamecoin.Wallet.SpendAsync(player, new SpendRequest { Currency = "gems", Amount = 30 });
var checkout = await gamecoin.Checkouts.CreateAsync(player, new CheckoutRequest { PackSku = "gems-500" });
Console.WriteLine($"gems: {granted.Balance.Total} -> {spent.Balance.Total}; the player pays at {checkout.CheckoutUrl}");
