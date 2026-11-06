using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Infrastructure.Caching;

namespace Pamplemousse.Infrastructure.Providers;

/// <summary>
/// Rental cars from the Booking COM API on RapidAPI (ADR 0016), the same cars and prices as cars.booking.com (spec 004).
/// One search returns every car at once, up to <see cref="MaxCars"/>, in the source's recommended order (research R-2).
/// </summary>
public sealed class BookingComCarProvider(HttpClient http, IOptions<ExternalApiOptions> options, IRequestQuota quota) : ICarProvider
{
    /// <summary>The source never returns more cars than this, so a full answer may have left some out (research R-2).</summary>
    public const int MaxCars = 500;

    private const string Feature = "Cars";

    public string Source => "Booking.com";

    public async Task<IReadOnlyList<PickUpPlace>> FindAirportsAsync(Destination destination, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var (doc, _) = await BookingComRequest.SendAsync(
            http, options.Value, quota, Feature, $"/car/auto-complete?query={Uri.EscapeDataString(destination.Name)}", ct);
        using (doc)
        {
            return [.. ((JsonElement?)doc.RootElement).Prop("data").Items()
                .Select(e => (JsonElement?)e)
                .Where(e => string.Equals(e.Prop("type").Str(), "airport", StringComparison.Ordinal))
                .Select(ToAirport)
                .OfType<PickUpPlace>()];
        }
    }

    public async Task<CarResults> SearchAsync(CarSearch search, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(search);
        var place = PlaceId(search.Place.Location);
        var time = CarSearch.PickUpTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"/car/search?pickUpId={Uri.EscapeDataString(place)}&dropOffId={Uri.EscapeDataString(place)}&pickUpDate={search.Dates.From:yyyy-MM-dd}&pickUpTime={time}&dropOffDate={search.Dates.To:yyyy-MM-dd}&dropOffTime={time}&driverAge={search.DriverAge}&currencyCode={search.Currency}");
        var (doc, raw) = await BookingComRequest.SendAsync(http, options.Value, quota, Feature, query, ct);
        using (doc)
        {
            var results = ((JsonElement?)doc.RootElement).Prop("data").Prop("search_results").Items().ToList();
            var cars = results
                .Select((e, i) => ToOffer(e, search, i + 1))
                .OfType<CarOffer>()
                .DistinctBy(c => c.Id, StringComparer.Ordinal)
                .ToList();
            return new CarResults(cars, results.Count >= MaxCars, Gzip(raw));
        }
    }

    /// <summary>The cars.booking.com search for the same place, dates and driver (research R-6). The source's own car links open "page not found".</summary>
    public string SearchUrl(Destination destination, CarSearch search)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(search);

        // The name only fills Booking.com's search box; the coordinates choose the cars. "City centre" alone would not say which city.
        var name = Uri.EscapeDataString(search.Place.Kind == PickUpKind.Airport ? search.Place.Name : destination.Name);
        var at = string.Create(CultureInfo.InvariantCulture, $"{search.Place.Location.Lat},{search.Place.Location.Lng}");
        var type = search.Place.Kind == PickUpKind.Airport ? "A" : "D";
        var (from, to) = (search.Dates.From, search.Dates.To);
        var hour = CarSearch.PickUpTime.Hour;
        var minute = CarSearch.PickUpTime.Minute;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"https://cars.booking.com/search-results?location=&dropLocation=&locationName={name}&dropLocationName={name}&coordinates={at}&dropCoordinates={at}&ftsType={type}&dropFtsType={type}&driversAge={search.DriverAge}&puDay={from.Day}&puMonth={from.Month}&puYear={from.Year}&puHour={hour}&puMinute={minute}&doDay={to.Day}&doMonth={to.Month}&doYear={to.Year}&doHour={hour}&doMinute={minute}&prefcurrency={search.Currency}&preflang=en");
    }

    /// <summary>The source names a place by its coordinates, base64-encoded JSON (research R-1), so no look-up is needed.</summary>
    internal static string PlaceId(GeoPoint location)
    {
        var json = string.Create(
            CultureInfo.InvariantCulture, $"{{\"latitude\":\"{location.Lat}\",\"longitude\":\"{location.Lng}\"}}");
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    private static PickUpPlace? ToAirport(JsonElement? e)
    {
        var iata = e.Prop("iata_code").Str();
        var lat = e.Prop("coordinates").Prop("latitude").Num();
        var lng = e.Prop("coordinates").Prop("longitude").Num();
        if (iata is not { Length: 3 } || !iata.All(char.IsAsciiLetter) || lat is null || lng is null)
        {
            return null;
        }

        return PickUpPlace.Airport(iata, e.Prop("name").Str() ?? iata, new GeoPoint(lat.Value, lng.Value));
    }

    private static CarOffer? ToOffer(JsonElement e, CarSearch search, int rank)
    {
        JsonElement? car = e;
        var vehicle = car.Prop("vehicle_info");
        var pricing = car.Prop("pricing_info");
        var pickUp = car.Prop("route_info").Prop("pickup");
        var id = vehicle.Prop("v_id").Str();
        var model = vehicle.Prop("v_name").Str();
        var company = car.Prop("supplier_info").Prop("name").Str();
        var lat = Double(pickUp.Prop("latitude"));
        var lng = Double(pickUp.Prop("longitude"));
        var price = CarPrice.For(
            Decimal(pricing.Prop("drive_away_price_before")),
            Decimal(pricing.Prop("drive_away_price")) ?? Decimal(pricing.Prop("price")),
            search.Days,
            pricing.Prop("currency").Str() is { Length: 3 } c ? c : search.Currency,
            string.Equals(pricing.Prop("pay_when").Str(), "PAY_NOW", StringComparison.Ordinal));
        if (id is null || model is null || company is null || lat is null || lng is null || price is null)
        {
            return null;
        }

        var rating = car.Prop("rating_info");
        var fees = pricing.Prop("fee_breakdown").Prop("known_fees").Items().Select(f => (JsonElement?)f).ToList();
        return new CarOffer(
            "bk:" + id,
            model,
            vehicle.Prop("group_or_similar").Str(),
            vehicle.Prop("group").Str() ?? "Other",
            vehicle.Prop("image_url").Str(),
            vehicle.Prop("transmission").Str() switch
            {
                "Automatic" => Gearbox.Automatic,
                "Manual" => Gearbox.Manual,
                _ => null,
            },
            Whole(vehicle.Prop("seats")),
            Whole(vehicle.Prop("doors")),
            Bags(car),
            Flag(vehicle.Prop("aircon")),
            vehicle.Prop("mileage").Str(),
            Flag(vehicle.Prop("unlimited_mileage")),
            vehicle.Prop("fuel_policy").Str(),
            new RentalCompany(company, car.Prop("supplier_info").Prop("logo_url").Str(), rating.Prop("average").Num(), rating.Prop("no_of_ratings").Int()),
            new PickUpPoint(
                pickUp.Prop("name").Str() ?? company,
                TypeOf(pickUp.Prop("location_type").Str()),
                pickUp.Prop("address").Str() is { Length: > 0 } a ? a : null,
                new GeoPoint(lat.Value, lng.Value),
                0),
            price,
            Flag(vehicle.Prop("free_cancellation")),
            Fee(fees, "DEPOSIT"),
            Fee(fees, "DAMAGE_EXCESS"),
            vehicle.Prop("badges").Prop("sponsored").Str() is { Length: > 0 },
            rank,
            car.Prop("forward_url").Str());
    }

    private static PickUpType TypeOf(string? type) => type switch
    {
        "IN_TERMINAL" => PickUpType.Terminal,
        "SHUTTLE_BUS" => PickUpType.Shuttle,
        "CAR_RENTAL_CENTRE" => PickUpType.RentalCentre,
        "TRAINSTATION" => PickUpType.TrainStation,
        "DOWNTOWN" => PickUpType.Downtown,
        _ => PickUpType.Other,
    };

    /// <summary>Checked bags, from the spec line "3 checked bags"; the source's suitcase counts are always 0 (research R-5).</summary>
    private static int? Bags(JsonElement? car)
    {
        var text = car.Prop("content").Prop("vehicleSpecs").Items()
            .Select(s => (JsonElement?)s)
            .FirstOrDefault(s => string.Equals(s.Prop("icon").Str(), "BAGS_LARGE", StringComparison.Ordinal))
            .Prop("text").Str();
        var digits = new string((text ?? string.Empty).TrimStart().TakeWhile(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var bags) ? bags : null;
    }

    private static Money? Fee(List<JsonElement?> fees, string type)
    {
        var fee = fees.FirstOrDefault(f => string.Equals(f.Prop("type").Str(), type, StringComparison.Ordinal));
        var amount = Decimal(fee.Prop("amount"));
        var currency = fee.Prop("currency").Str();
        return amount is > 0 && currency is { Length: 3 } && currency.All(char.IsAsciiLetter) ? new Money(amount.Value, currency) : null;
    }

    private static bool? Flag(JsonElement? e) => e.Int() switch
    {
        1 => true,
        0 => false,
        _ => e?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        },
    };

    /// <summary>A whole number sent as a number or a string ("5"). Anything else, such as "5+", is unknown.</summary>
    private static int? Whole(JsonElement? e) =>
        e.Int() ?? (int.TryParse(e.Str(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null);

    private static double? Double(JsonElement? e) =>
        e.Num() ?? (double.TryParse(e.Str(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null);

    private static decimal? Decimal(JsonElement? e) =>
        e is { ValueKind: JsonValueKind.Number } v && v.TryGetDecimal(out var d) ? d : null;

    private static byte[] Gzip(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(raw);
        }

        return output.ToArray();
    }
}
