using System.Text.Json;

namespace Pamplemousse.Infrastructure.Providers;

/// <summary>Null-tolerant reads for provider JSON, where most fields are optional.</summary>
internal static class JsonElementExtensions
{
    public static JsonElement? Prop(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

    public static JsonElement? Prop(this JsonElement? e, string name) => e?.Prop(name);

    public static string? Str(this JsonElement? e) => e is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    public static double? Num(this JsonElement? e) => e is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : null;

    public static int? Int(this JsonElement? e) => e is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var i) ? i : null;

    public static IEnumerable<JsonElement> Items(this JsonElement? e) =>
        e is { ValueKind: JsonValueKind.Array } v ? v.EnumerateArray() : [];

    public static JsonElement? First(this JsonElement? e) => e.Items().Select(x => (JsonElement?)x).FirstOrDefault();
}
