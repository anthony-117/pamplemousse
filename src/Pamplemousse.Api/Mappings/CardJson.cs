using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Pamplemousse.Domain.Itinerary;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.TravelEvents;

namespace Pamplemousse.Api.Mappings;

/// <summary>
/// Cards as the app receives them (spec 010, contracts/chat-api.md "Cards"): one envelope (kind, id, position, when it was
/// found, its city) around the item exactly as its list endpoint writes it (research R-4).
/// </summary>
internal static class CardJson
{
    /// <summary>The API's own JSON: camelCase names and enum values, as the controllers write them.</summary>
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static JsonObject Envelope(
        string kind,
        string id,
        double lat,
        double lng,
        bool approximate,
        DateTimeOffset foundAt,
        string? destinationId,
        string? destinationName,
        object item) => new()
        {
            ["kind"] = kind,
            ["id"] = id,
            ["lat"] = lat,
            ["lng"] = lng,
            ["approximate"] = approximate,
            ["foundAt"] = foundAt,
            ["destinationId"] = destinationId,
            ["destinationName"] = destinationName,
            ["item"] = JsonSerializer.SerializeToNode(item, Options),
        };

    /// <summary>A day alternative: a place card, or an event card at the city centre (an event has no position of its own).</summary>
    public static JsonObject Of(EditItem item, PlanDestination city, DateTimeOffset foundAt)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(city);
        return item.Place is { } place ? Of(place, city.Id, city.Name, foundAt) : Of(item.Event!, city, foundAt);
    }

    public static JsonObject Of(Place place, string destinationId, string destinationName, DateTimeOffset foundAt)
    {
        ArgumentNullException.ThrowIfNull(place);
        return Envelope("place", place.Id, place.Location.Lat, place.Location.Lng, false, foundAt, destinationId, destinationName, place.ToViewModel());
    }

    public static JsonObject Of(TravelEvent travelEvent, PlanDestination city, DateTimeOffset foundAt)
    {
        ArgumentNullException.ThrowIfNull(travelEvent);
        ArgumentNullException.ThrowIfNull(city);
        return Envelope("event", travelEvent.Id, city.Centre.Lat, city.Centre.Lng, true, foundAt, city.Id, city.Name, travelEvent.ToViewModel());
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
