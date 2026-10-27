using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Places;
using Pamplemousse.Infrastructure.Caching;

namespace Pamplemousse.Infrastructure.Providers;

/// <summary>
/// Google Places API (New), GOOGLE_PLACES_API_KEY: restaurants in a city and place details.
/// Field masks keep requests to the fields we show; rating fields put the call in the Enterprise SKU (see the budget study).
/// </summary>
public sealed class GooglePlacesRestaurantProvider(HttpClient http, JsonCache cache, IOptions<ExternalApiOptions> options) : IPlaceProvider
{
    private const string SearchFields = "places.id,places.displayName,places.location,places.rating,places.userRatingCount,places.priceLevel,places.formattedAddress,places.primaryTypeDisplayName";
    private const string DetailFields = "id,displayName,location,rating,userRatingCount,priceLevel,formattedAddress,primaryTypeDisplayName,websiteUri,regularOpeningHours.weekdayDescriptions,editorialSummary";
    private const int MaxResults = 20;

    public string Source => "gp";

    public PlaceCategory Category => PlaceCategory.Restaurant;

    private string ApiKey => options.Value.GooglePlacesApiKey is { Length: > 0 } key
        ? key
        : throw new ProviderNotConfiguredException("Google Places", "GOOGLE_PLACES_API_KEY");

    public Task<IReadOnlyList<Place>> SearchAsync(Destination destination, int limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var key = ApiKey;

        return cache.GetOrCreateAsync<IReadOnlyList<Place>>($"places:gp:{destination.Id}:{limit}", TimeSpan.FromHours(24), async token =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://places.googleapis.com/v1/places:searchText"))
            {
                Content = JsonContent.Create(new
                {
                    textQuery = $"restaurants in {destination.Name}, {destination.Region}",
                    includedType = "restaurant",
                    maxResultCount = Math.Min(limit, MaxResults),
                    locationBias = new { circle = new { center = new { latitude = destination.Location.Lat, longitude = destination.Location.Lng }, radius = 10_000.0 } },
                }),
            };
            request.Headers.Add("X-Goog-Api-Key", key);
            request.Headers.Add("X-Goog-FieldMask", SearchFields);

            using var response = await http.SendAsync(request, token);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return ((JsonElement?)doc.RootElement).Prop("places").Items().Select(p => ToPlace(p)).ToList();
        }, ct);
    }

    public Task<Place?> GetAsync(string providerId, CancellationToken ct)
    {
        var key = ApiKey;
        return cache.GetOrCreateAsync<Place?>($"place:gp:{providerId}", TimeSpan.FromHours(24), async token =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://places.googleapis.com/v1/places/{Uri.EscapeDataString(providerId)}"));
            request.Headers.Add("X-Goog-Api-Key", key);
            request.Headers.Add("X-Goog-FieldMask", DetailFields);

            using var response = await http.SendAsync(request, token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return ToPlace(doc.RootElement);
        }, ct);
    }

    private static Place ToPlace(JsonElement? p)
    {
        var location = p.Prop("location");
        return new Place(
            $"gp:{p.Prop("id").Str()}",
            p.Prop("displayName").Prop("text").Str() ?? "",
            PlaceCategory.Restaurant,
            new GeoPoint(location.Prop("latitude").Num() ?? 0, location.Prop("longitude").Num() ?? 0),
            p.Prop("primaryTypeDisplayName").Prop("text").Str() ?? "Restaurant",
            p.Prop("rating").Num(),
            p.Prop("userRatingCount").Int(),
            PriceLevel(p.Prop("priceLevel").Str()),
            p.Prop("formattedAddress").Str(),
            p.Prop("editorialSummary").Prop("text").Str(),
            ImageUrl: null, // Google photo URLs need the API key; a proxy endpoint comes later.
            p.Prop("websiteUri").Str(),
            [.. p.Prop("regularOpeningHours").Prop("weekdayDescriptions").Items().Select(d => d.GetString() ?? "")],
            "Google Places");
    }

    private static string? PriceLevel(string? level) => level switch
    {
        "PRICE_LEVEL_FREE" => "Free",
        "PRICE_LEVEL_INEXPENSIVE" => "$",
        "PRICE_LEVEL_MODERATE" => "$$",
        "PRICE_LEVEL_EXPENSIVE" => "$$$",
        "PRICE_LEVEL_VERY_EXPENSIVE" => "$$$$",
        _ => null,
    };
}
