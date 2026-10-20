using System;
using System.Text.Json;

namespace Apilow.GameCoin.Internal;

// The shape checks of the catalog management responses.
internal static partial class Shape
{
    public static void ManagedCurrency(JsonElement value, string path)
    {
        CatalogCurrency(value, path);
        Str(value, "status", path);
        Date(value, "createdAt", path);
        Date(value, "updatedAt", path);
    }

    public static void ManagedItem(JsonElement value, string path)
    {
        CatalogItem(value, path);
        Str(value, "status", path);
        Date(value, "createdAt", path);
        Date(value, "updatedAt", path);
    }

    public static void ManagedPack(JsonElement value, string path)
    {
        var o = Obj(value, path);
        Str(o, "sku", path);
        Str(o, "name", path);
        StrOrNull(o, "description", path);
        Int(o, "priceCents", path);
        Str(o, "currency", path);
        List(o, "grants", path, (grant, grantPath) =>
        {
            var g = Obj(grant, grantPath);
            Str(g, "currency", grantPath);
            Int(g, "amount", grantPath);
            Int(g, "bonus", grantPath);
        });
        List(o, "items", path, (line, linePath) =>
        {
            var l = Obj(line, linePath);
            Str(l, "sku", linePath);
            Int(l, "quantity", linePath);
        });
        StrOrNull(o, "badge", path);
        IntOrNull(o, "maxPerPlayer", path);
        Int(o, "sortOrder", path);
        Str(o, "status", path);
        if (Given(o, "availability"))
        {
            var availability = Obj(Prop(o, "availability"), $"{path}.availability");
            DateOrNull(availability, "startsAt", $"{path}.availability");
            DateOrNull(availability, "endsAt", $"{path}.availability");
            IntOrNull(availability, "stock", $"{path}.availability");
            Bool(availability, "founder", $"{path}.availability");
            Int(availability, "sold", $"{path}.availability");
        }

        Date(o, "createdAt", path);
        Date(o, "updatedAt", path);
    }

    public static void ManagedList(JsonElement value, string key, Action<JsonElement, string> item)
    {
        List(Obj(value, "response"), key, "response", item);
    }
}
