using System.Globalization;
using System.Text.Json;

namespace Pamplemousse.Application.Assistant.Reply;

/// <summary>What the server reads back from a card's JSON: to save it, or to group a reply's cards.</summary>
internal sealed record CardInfo(string Kind, string Id, string? DestinationId, string Name, string? ImageUrl, double Lat, double Lng, string? ItemJson)
{
    public static CardInfo Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        double Num(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        var item = root.TryGetProperty("item", out var i) && i.ValueKind == JsonValueKind.Object ? i : default;
        var hasItem = item.ValueKind == JsonValueKind.Object;
        var name = hasItem ? Str(item, "name") ?? Str(item, "model") ?? "" : "";
        var image = hasItem ? Str(item, "imageUrl") ?? FirstPhoto(item) : null;
        return new CardInfo(
            Str(root, "kind") ?? "",
            Str(root, "id") ?? "",
            Str(root, "destinationId"),
            name,
            image,
            Num("lat"),
            Num("lng"),
            hasItem ? item.GetRawText() : null);
    }

    private static string? FirstPhoto(JsonElement item) =>
        item.TryGetProperty("photoUrls", out var photos) && photos.ValueKind == JsonValueKind.Array && photos.GetArrayLength() > 0
            ? photos[0].GetString()
            : item.TryGetProperty("photoUrl", out var photo) && photo.ValueKind == JsonValueKind.String ? photo.GetString() : null;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Kind}:{Id}");
}
