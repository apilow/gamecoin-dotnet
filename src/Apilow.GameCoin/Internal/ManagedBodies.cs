using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Apilow.GameCoin.Internal;

/// <summary>Request bodies of the catalog management: checked for shape before any request, written by hand.</summary>
internal static class ManagedBodies
{
    private static readonly string[] Badges = { "popular", "best_value" };

    public static byte[] CreateCurrency(CreateCurrencyRequest request)
    {
        Guard.NotNull(request, nameof(request));
        Guard.NotEmpty(request.Code, "The currency code", nameof(request));
        Guard.NotEmpty(request.Name, "The currency name", nameof(request));
        return BodyWriter.Write(w =>
        {
            w.WriteString("code", request.Code);
            w.WriteString("name", request.Name);
            if (request.Purchasable is { } purchasable)
            {
                w.WriteBoolean("purchasable", purchasable);
            }
        });
    }

    public static byte[] UpdateCurrency(UpdateCurrencyRequest? request)
    {
        if (request is null)
        {
            return BodyWriter.Empty();
        }

        Guard.NotEmptyIfGiven(request.Name, "The currency name", nameof(request));
        return BodyWriter.Write(w =>
        {
            w.WriteIfNotNull("name", request.Name);
            if (request.Purchasable is { } purchasable)
            {
                w.WriteBoolean("purchasable", purchasable);
            }
        });
    }

    public static byte[] CreateItem(CreateItemRequest request)
    {
        Guard.NotNull(request, nameof(request));
        Guard.NotEmpty(request.Sku, "The item sku", nameof(request));
        Guard.NotEmpty(request.Name, "The item name", nameof(request));
        if (request.Type != ItemType.Consumable && request.Type != ItemType.Durable)
        {
            throw new ArgumentException("The item Type must be ItemType.Consumable or ItemType.Durable.", nameof(request));
        }

        if (request.NoMaxOwned && request.MaxOwned is not null)
        {
            throw new ArgumentException("MaxOwned and NoMaxOwned are exclusive.", nameof(request));
        }

        CheckMaxOwned(request.MaxOwned, nameof(request));
        if (request.Price is not null)
        {
            CheckPrice(request.Price, nameof(request));
        }

        return BodyWriter.Write(w =>
        {
            w.WriteString("sku", request.Sku);
            w.WriteString("name", request.Name);
            w.WriteString("type", request.Type);
            w.WriteIfNotNull("description", request.Description);
            if (request.Price is { } price)
            {
                WritePrice(w, price);
            }

            if (request.NoMaxOwned)
            {
                w.WriteNull("maxOwned");
            }
            else if (request.MaxOwned is { } maxOwned)
            {
                w.WriteNumber("maxOwned", maxOwned);
            }

            if (request.ClientPurchasable)
            {
                w.WriteBoolean("clientPurchasable", true);
            }

            w.WriteMetadata(request.Metadata);
        });
    }

    public static byte[] UpdateItem(UpdateItemRequest? request)
    {
        if (request is null)
        {
            return BodyWriter.Empty();
        }

        Guard.NotEmptyIfGiven(request.Name, "The item name", nameof(request));
        CheckNotBoth(request.Description is not null, ItemFields.Description, request.Clear, nameof(request));
        CheckNotBoth(request.Price is not null, ItemFields.Price, request.Clear, nameof(request));
        CheckNotBoth(request.MaxOwned is not null, ItemFields.MaxOwned, request.Clear, nameof(request));
        CheckMaxOwned(request.MaxOwned, nameof(request));
        if (request.Price is not null)
        {
            CheckPrice(request.Price, nameof(request));
        }

        return BodyWriter.Write(w =>
        {
            w.WriteIfNotNull("name", request.Name);
            if (request.Description is not null)
            {
                w.WriteString("description", request.Description);
            }
            else if (request.Clear.HasFlag(ItemFields.Description))
            {
                w.WriteNull("description");
            }

            if (request.Price is { } price)
            {
                WritePrice(w, price);
            }
            else if (request.Clear.HasFlag(ItemFields.Price))
            {
                w.WriteNull("price");
            }

            if (request.MaxOwned is { } maxOwned)
            {
                w.WriteNumber("maxOwned", maxOwned);
            }
            else if (request.Clear.HasFlag(ItemFields.MaxOwned))
            {
                w.WriteNull("maxOwned");
            }

            if (request.ClientPurchasable is { } clientPurchasable)
            {
                w.WriteBoolean("clientPurchasable", clientPurchasable);
            }

            w.WriteMetadata(request.Metadata);
        });
    }

    public static byte[] CreatePack(CreatePackRequest request)
    {
        Guard.NotNull(request, nameof(request));
        Guard.NotEmpty(request.Sku, "The pack sku", nameof(request));
        Guard.NotEmpty(request.Name, "The pack name", nameof(request));
        Guard.InRange(request.PriceCents, 0, long.MaxValue, "PriceCents", nameof(request));
        CheckGrants(request.Grants, nameof(request));
        CheckPackItems(request.Items, nameof(request));
        if (request.Badge is not null)
        {
            CheckBadge(request.Badge, nameof(request));
        }

        if (request.MaxPerPlayer is { } maxPerPlayer)
        {
            Guard.Positive(maxPerPlayer, "MaxPerPlayer", nameof(request));
        }

        CheckSortOrder(request.SortOrder, nameof(request));
        CheckAvailability(request.Availability, nameof(request));
        if (request.Status is not null && request.Status != PackStatus.Draft && request.Status != PackStatus.Active)
        {
            throw new ArgumentException("The Status of a new pack must be PackStatus.Draft or PackStatus.Active.", nameof(request));
        }

        return BodyWriter.Write(w =>
        {
            w.WriteString("sku", request.Sku);
            w.WriteString("name", request.Name);
            w.WriteNumber("priceCents", request.PriceCents);
            w.WriteIfNotNull("description", request.Description);
            WriteGrants(w, request.Grants);
            WritePackItems(w, request.Items);
            w.WriteIfNotNull("badge", request.Badge);
            if (request.MaxPerPlayer is { } max)
            {
                w.WriteNumber("maxPerPlayer", max);
            }

            if (request.SortOrder is { } sort)
            {
                w.WriteNumber("sortOrder", sort);
            }

            WriteAvailability(w, request.Availability);
            w.WriteIfNotNull("status", request.Status);
        });
    }

    public static byte[] UpdatePack(UpdatePackRequest? request)
    {
        if (request is null)
        {
            return BodyWriter.Empty();
        }

        Guard.NotEmptyIfGiven(request.Name, "The pack name", nameof(request));
        CheckNotBoth(request.Description is not null, PackFields.Description, request.Clear, nameof(request));
        CheckNotBoth(request.Badge is not null, PackFields.Badge, request.Clear, nameof(request));
        CheckNotBoth(request.MaxPerPlayer is not null, PackFields.MaxPerPlayer, request.Clear, nameof(request));
        if (request.PriceCents is { } price)
        {
            Guard.InRange(price, 0, long.MaxValue, "PriceCents", nameof(request));
        }

        CheckGrants(request.Grants, nameof(request));
        CheckPackItems(request.Items, nameof(request));
        if (request.Badge is not null)
        {
            CheckBadge(request.Badge, nameof(request));
        }

        if (request.MaxPerPlayer is { } maxPerPlayer)
        {
            Guard.Positive(maxPerPlayer, "MaxPerPlayer", nameof(request));
        }

        CheckSortOrder(request.SortOrder, nameof(request));
        CheckAvailability(request.Availability, nameof(request));
        if (request.Status is not null && request.Status != PackStatus.Draft && request.Status != PackStatus.Active && request.Status != PackStatus.Archived)
        {
            throw new ArgumentException("The Status must be PackStatus.Draft, PackStatus.Active or PackStatus.Archived.", nameof(request));
        }

        return BodyWriter.Write(w =>
        {
            w.WriteIfNotNull("name", request.Name);
            if (request.Description is not null)
            {
                w.WriteString("description", request.Description);
            }
            else if (request.Clear.HasFlag(PackFields.Description))
            {
                w.WriteNull("description");
            }

            if (request.PriceCents is { } cents)
            {
                w.WriteNumber("priceCents", cents);
            }

            WriteGrants(w, request.Grants);
            WritePackItems(w, request.Items);
            if (request.Badge is not null)
            {
                w.WriteString("badge", request.Badge);
            }
            else if (request.Clear.HasFlag(PackFields.Badge))
            {
                w.WriteNull("badge");
            }

            if (request.MaxPerPlayer is { } max)
            {
                w.WriteNumber("maxPerPlayer", max);
            }
            else if (request.Clear.HasFlag(PackFields.MaxPerPlayer))
            {
                w.WriteNull("maxPerPlayer");
            }

            if (request.SortOrder is { } sort)
            {
                w.WriteNumber("sortOrder", sort);
            }

            WriteAvailability(w, request.Availability);
            w.WriteIfNotNull("status", request.Status);
        });
    }

    /// <summary><c>?includeArchived=true</c> when asked; nothing otherwise, so that the default URL stays plain.</summary>
    public static string? ListQuery(bool includeArchived) => includeArchived ? "?includeArchived=true" : null;

    // --- checks ---------------------------------------------------------------------------------------------------

    private static void CheckNotBoth<TFlags>(bool isSet, TFlags field, TFlags clear, string paramName)
        where TFlags : struct, Enum
    {
        if (isSet && clear.HasFlag(field))
        {
            throw new ArgumentException($"The field {field} cannot be both set and cleared.", paramName);
        }
    }

    private static void CheckMaxOwned(long? value, string paramName)
    {
        if (value is { } v)
        {
            Guard.Positive(v, "MaxOwned", paramName);
        }
    }

    private static void CheckPrice(ItemPrice price, string paramName)
    {
        Guard.NotEmpty(price.Currency, "The price currency", paramName);
        Guard.Positive(price.Amount, "The price Amount", paramName);
    }

    private static void CheckGrants(IReadOnlyList<PackGrant>? grants, string paramName)
    {
        foreach (var grant in grants ?? Array.Empty<PackGrant>())
        {
            Guard.NotEmpty(grant?.Currency, "A grant currency", paramName);
            Guard.InRange(grant!.Amount, 0, long.MaxValue, "A grant Amount", paramName);
            Guard.InRange(grant.Bonus, 0, long.MaxValue, "A grant Bonus", paramName);
        }
    }

    private static void CheckPackItems(IReadOnlyList<PackItem>? items, string paramName)
    {
        foreach (var item in items ?? Array.Empty<PackItem>())
        {
            Guard.NotEmpty(item?.Sku, "A pack item sku", paramName);
            Guard.Positive(item!.Quantity, "A pack item Quantity", paramName);
        }
    }

    private static void CheckBadge(string badge, string paramName)
    {
        if (Array.IndexOf(Badges, badge) < 0)
        {
            throw new ArgumentException("The Badge must be \"popular\" or \"best_value\".", paramName);
        }
    }

    private static void CheckSortOrder(long? sortOrder, string paramName)
    {
        if (sortOrder is { } order)
        {
            Guard.InRange(order, -(1L << 53), 1L << 53, "SortOrder", paramName);
        }
    }

    private static void CheckAvailability(PackAvailabilityRequest? availability, string paramName)
    {
        if (availability?.Stock is { } stock)
        {
            Guard.Positive(stock, "The availability Stock", paramName);
        }
    }

    // --- writers --------------------------------------------------------------------------------------------------

    private static void WritePrice(Utf8JsonWriter w, ItemPrice price)
    {
        w.WriteStartObject("price");
        w.WriteString("currency", price.Currency);
        w.WriteNumber("amount", price.Amount);
        w.WriteEndObject();
    }

    private static void WriteGrants(Utf8JsonWriter w, IReadOnlyList<PackGrant>? grants)
    {
        if (grants is null)
        {
            return;
        }

        w.WriteStartArray("grants");
        foreach (var grant in grants)
        {
            w.WriteStartObject();
            w.WriteString("currency", grant.Currency);
            w.WriteNumber("amount", grant.Amount);
            w.WriteNumber("bonus", grant.Bonus);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void WritePackItems(Utf8JsonWriter w, IReadOnlyList<PackItem>? items)
    {
        if (items is null)
        {
            return;
        }

        w.WriteStartArray("items");
        foreach (var item in items)
        {
            w.WriteStartObject();
            w.WriteString("sku", item.Sku);
            w.WriteNumber("quantity", item.Quantity);
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private static void WriteAvailability(Utf8JsonWriter w, PackAvailabilityRequest? availability)
    {
        if (availability is null)
        {
            return;
        }

        w.WriteStartObject("availability");
        WriteInstant(w, "startsAt", availability.StartsAt);
        WriteInstant(w, "endsAt", availability.EndsAt);
        if (availability.Stock is { } stock)
        {
            w.WriteNumber("stock", stock);
        }
        else
        {
            w.WriteNull("stock");
        }

        w.WriteBoolean("founder", availability.Founder);
        w.WriteEndObject();
    }

    private static void WriteInstant(Utf8JsonWriter w, string name, DateTimeOffset? instant)
    {
        if (instant is { } value)
        {
            w.WriteString(name, value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture));
        }
        else
        {
            w.WriteNull(name);
        }
    }
}
