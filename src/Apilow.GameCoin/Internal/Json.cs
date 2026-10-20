using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apilow.GameCoin.Internal;

// Wrappers of the composed responses that have no public type of their own.
internal sealed class InventoryListResponse
{
    public IReadOnlyList<InventoryItem> Items { get; init; } = Array.Empty<InventoryItem>();
}

internal sealed class InventoryChangeResponse
{
    public InventoryItem Item { get; init; } = new();
}

internal sealed class CurrencyListResponse
{
    public IReadOnlyList<ManagedCurrency> Currencies { get; init; } = Array.Empty<ManagedCurrency>();
}

internal sealed class ItemListResponse
{
    public IReadOnlyList<ManagedItem> Items { get; init; } = Array.Empty<ManagedItem>();
}

internal sealed class PackListResponse
{
    public IReadOnlyList<ManagedPack> Packs { get; init; } = Array.Empty<ManagedPack>();
}

/// <summary>Source-generated (de)serialisation of the responses: no reflection, safe for trimming, AOT and IL2CPP.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Catalog))]
[JsonSerializable(typeof(Player))]
[JsonSerializable(typeof(PlayerToken))]
[JsonSerializable(typeof(Wallet))]
[JsonSerializable(typeof(Movement))]
[JsonSerializable(typeof(LedgerPage))]
[JsonSerializable(typeof(InventoryListResponse))]
[JsonSerializable(typeof(InventoryChangeResponse))]
[JsonSerializable(typeof(Purchase))]
[JsonSerializable(typeof(Checkout))]
[JsonSerializable(typeof(Order))]
[JsonSerializable(typeof(Redemption))]
[JsonSerializable(typeof(ManagedCurrency))]
[JsonSerializable(typeof(ManagedItem))]
[JsonSerializable(typeof(ManagedPack))]
[JsonSerializable(typeof(CurrencyListResponse))]
[JsonSerializable(typeof(ItemListResponse))]
[JsonSerializable(typeof(PackListResponse))]
[JsonSerializable(typeof(WalletCreditedData))]
[JsonSerializable(typeof(WebhookTestData))]
internal sealed partial class GameCoinJsonContext : JsonSerializerContext
{
}

/// <summary>Hand-written request bodies (the API is strict: an absent field and a <c>null</c> field are not the same thing).</summary>
internal static class BodyWriter
{
    public static byte[] Write(Action<Utf8JsonWriter> fill)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            fill(writer);
            writer.WriteEndObject();
        }

        return stream.ToArray();
    }

    public static byte[] Empty() => Write(_ => { });

    public static void WriteIfNotNull(this Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    public static void WriteMetadata(this Utf8JsonWriter writer, IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        writer.WriteStartObject("metadata");
        foreach (var pair in metadata)
        {
            writer.WriteString(pair.Key, pair.Value);
        }

        writer.WriteEndObject();
    }
}
