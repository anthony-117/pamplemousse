using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;

namespace Pamplemousse.Api.Tests;

/// <summary>Stands in for the real car source, so endpoint tests never call the internet or use the request quota.</summary>
public sealed class FakeCars : ICarProvider
{
    public Exception? Error { get; set; }

    public int Searches { get; private set; }

    public int AirportLookUps { get; private set; }

    public string Source => "Booking.com";

    public Task<IReadOnlyList<PickUpPlace>> FindAirportsAsync(Destination destination, CancellationToken ct)
    {
        AirportLookUps++;
        IReadOnlyList<PickUpPlace> airports =
        [
            PickUpPlace.Airport("FCO", "Fiumicino Airport", new GeoPoint(41.7927, 12.2523)),
            PickUpPlace.Airport("YQM", "Moncton Airport", new GeoPoint(46.1122, -64.6786)),
        ];
        return Error is null ? Task.FromResult(airports) : Task.FromException<IReadOnlyList<PickUpPlace>>(Error);
    }

    public Task<CarResults> SearchAsync(CarSearch search, CancellationToken ct)
    {
        Searches++;
        if (Error is not null)
        {
            return Task.FromException<CarResults>(Error);
        }

        IReadOnlyList<CarOffer> cars =
        [
            Car("bk:1", "Fiat 600", "Intermediate", Gearbox.Automatic, 5, 3, "Budget", 7.0, PickUpType.Downtown, 300, 1, search),
            Car("bk:2", "Fiat Panda", "Mini", Gearbox.Manual, 4, 1, "Avis", 8.1, PickUpType.TrainStation, 120, 2, search),
            Car("bk:3", "Fiat Panda", "Mini", Gearbox.Manual, 4, 1, "Avis", 8.1, PickUpType.TrainStation, 150, 3, search),
        ];
        return Task.FromResult(new CarResults(cars, false, [1, 2, 3]));
    }

    public string SearchUrl(Destination destination, CarSearch search) => $"https://cars.booking.com/search-results?driversAge={search.DriverAge}";

    private static CarOffer Car(
        string id, string model, string size, Gearbox gearbox, int seats, int bags, string company, double rating, PickUpType type, decimal total,
        int rank, CarSearch search) => new(
        id,
        model,
        "or similar",
        size,
        "https://cdn.rcstatic.com/1.png",
        gearbox,
        seats,
        4,
        bags,
        true,
        "Unlimited km",
        true,
        "Like for like",
        new RentalCompany(company, null, rating, 100),
        new PickUpPoint(company + " desk", type, "Via Roma 1", new GeoPoint(41.9, 12.49), 0),
        CarPrice.For(total, null, search.Days, search.Currency, payNow: true)!,
        true,
        new Money(200, "EUR"),
        null,
        Sponsored: rank == 1,
        rank,
        null);
}
