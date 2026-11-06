using System.Globalization;
using System.Text.Json;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Cars.Queries.GetCarPlaces;
using Pamplemousse.Application.Cars.Queries.SearchCars;
using Pamplemousse.Application.Ranking;
using Pamplemousse.Domain.Cars;
using Pamplemousse.Domain.Common;

namespace Pamplemousse.Api.Mappings;

internal static class CarMappings
{
    public static CarPlacesViewModel ToViewModel(this GetCarPlacesResponse r) => new(
        [.. r.Places.Select(p => new CarPlaceViewModel(p.Id, p.Kind, p.Name, p.Iata, new GeoPointViewModel(p.Location.Lat, p.Location.Lng)))],
        r.Warnings);

    public static CarSearchViewModel ToViewModel(this SearchCarsResponse r)
    {
        var time = CarSearch.PickUpTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        return new CarSearchViewModel(
            new CarDestinationViewModel(r.Destination.Id, r.Destination.Name),
            new CarSearchPlaceViewModel(r.Place.Id, r.Place.Kind, r.Place.Name, r.Place.Iata),
            new RentalDatesViewModel(r.Dates.From, r.Dates.To, r.Days, time, time),
            r.DriverAge,
            r.Currency,
            r.Sort,
            r.Page,
            r.TotalItems,
            r.TotalPages,
            r.PricesFetchedAt,
            r.IsSavedPrice,
            r.MayHaveMore,
            [.. r.Items.Select(c => c.ToViewModel(r.BookingUrl, r.Source, r.Ranking))],
            new CarFilterOptionsViewModel(
                Options(r.FilterOptions.Sizes, v => v),
                Options(r.FilterOptions.Companies, v => v),

                // Pick-up types go back in the query as the same camelCase names the cars use.
                Options(r.FilterOptions.PickUpTypes, JsonNamingPolicy.CamelCase.ConvertName)),
            r.DriverNote,
            r.Message,
            r.SearchUrl,
            r.Warnings,
            MatchMappings.WeightsOf(r.Ranking),
            r.Ranking?.TripBudget,
            r.Ranking?.Note);
    }

    private static CarViewModel ToViewModel(this CarOffer c, string bookingUrl, string source, RankingResult? ranking) => new(
        c.Id,
        c.Model,
        c.OrSimilar,
        c.Size,
        c.PhotoUrl,
        c.Gearbox,
        c.Seats,
        c.Doors,
        c.Bags,
        c.AirCon,
        c.Mileage,
        c.UnlimitedMileage,
        c.FuelPolicy,
        new RentalCompanyViewModel(c.Company.Name, c.Company.LogoUrl, c.Company.Rating, c.Company.ReviewCount),
        new CarPickUpViewModel(c.PickUp.Name, c.PickUp.Type, c.PickUp.Address, new GeoPointViewModel(c.PickUp.Location.Lat, c.PickUp.Location.Lng), c.PickUp.DistanceKm),
        new CarPriceViewModel(c.Price.Total, c.Price.PerDay, c.Price.Currency, c.Price.PayNow),
        c.FreeCancellation,
        Money(c.Deposit),
        Money(c.DamageExcess),
        c.Sponsored,
        bookingUrl,
        source,
        MatchMappings.MatchOf(ranking, c.Id));

    private static MoneyViewModel? Money(Money? m) => m is null ? null : new MoneyViewModel(m.Amount, m.Currency);

    private static List<FilterOptionViewModel> Options(IReadOnlyList<FilterOption> options, Func<string, string> name) =>
        [.. options.Select(o => new FilterOptionViewModel(name(o.Value), o.Count))];
}
