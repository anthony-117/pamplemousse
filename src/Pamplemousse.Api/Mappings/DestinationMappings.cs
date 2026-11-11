using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Destinations;
using Pamplemousse.Application.Destinations.Queries.GetDestination;
using Pamplemousse.Application.Destinations.Queries.GetOverview;
using Pamplemousse.Application.Destinations.Queries.ListCountries;
using Pamplemousse.Application.Destinations.Queries.ListEvents;
using Pamplemousse.Application.Destinations.Queries.ListPlaces;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Places;
using Pamplemousse.Domain.TravelEvents;

namespace Pamplemousse.Api.Mappings;

/// <summary>Application results → JSON shapes. Written by hand so a renamed field is a compile error, not a wrong response.</summary>
internal static class DestinationMappings
{
    public static DestinationSummaryViewModel ToViewModel(this DestinationSummary s) => new(
        s.Id, s.Name, s.Region, s.CountryCode, s.CountryName, s.Flag, s.Lat, s.Lng, s.Population, s.TimeZone);

    public static DestinationDetailsViewModel ToViewModel(this DestinationDetailsResponse r) => new(r.Destination.ToViewModel(), r.Country?.ToViewModel());

    public static CountryOptionViewModel ToViewModel(this CountryOption c) => new(c.Code, c.Name, c.Flag, c.Currencies);

    public static OverviewViewModel ToViewModel(this OverviewResponse r) => new(
        r.Destination.ToViewModel(),
        r.Country?.ToViewModel(),
        r.Dates.ToViewModel(),
        r.Weather?.ToViewModel(),
        r.Exchange?.ToViewModel(),
        r.Holidays is null ? null : [.. r.Holidays.Select(h => new HolidayViewModel(h.Date, h.Name, h.LocalName))],
        r.Visa is { } v ? new VisaRequirementViewModel(v.Passport, v.Destination, v.Requirement, v.VisaFreeDays) : null,
        r.Advisory is { } a ? new TravelAdvisoryViewModel(a.Level, a.Label, a.Summary, a.SourceUrl) : null,
        r.Warnings);

    public static PlacesViewModel ToViewModel(this PlacesResponse r) => new(r.Category, [.. r.Items.Select(ToViewModel)], r.Warnings);

    public static PlaceViewModel ToViewModel(this Place p) => new(
        p.Id,
        p.Name,
        p.Category,
        new GeoPointViewModel(p.Location.Lat, p.Location.Lng),
        p.Kind,
        p.Rating,
        p.RatingCount,
        p.PriceLevel,
        p.Address,
        p.Description,
        p.ImageUrl,
        p.Website,
        p.OpeningHours,
        p.Source);

    public static EventsViewModel ToViewModel(this EventsResponse r) => new(r.Dates.ToViewModel(), [.. r.Items.Select(ToViewModel)], r.Warnings);

    public static EventViewModel ToViewModel(this TravelEvent e) => new(
        e.Id,
        e.Name,
        e.Date,
        e.Time,
        e.Venue,
        e.Category,
        e.PriceFrom is { } price ? new MoneyViewModel(price.Amount, price.Currency) : null,
        e.ImageUrl,
        e.Url,
        e.Source);

    private static CountryViewModel ToViewModel(this Country c) => new(
        c.Code,
        c.Name,
        c.OfficialName,
        c.Capital,
        c.Region,
        c.Subregion,
        [.. c.Currencies.Select(x => new CurrencyViewModel(x.Code, x.Name, x.Symbol))],
        c.Languages,
        c.CallingCode,
        c.Flag);

    private static DateRangeViewModel ToViewModel(this DateRange d) => new(d.From, d.To);

    private static WeatherReportViewModel ToViewModel(this WeatherReport w) => new(
        w.Kind, [.. w.Days.Select(d => new WeatherDayViewModel(d.Date, d.MaxC, d.MinC, d.RainChance, d.Code, d.Summary))]);

    private static ExchangeRateViewModel ToViewModel(this ExchangeRate e) => new(e.Base, e.Quote, e.Rate, e.Date, e.Source);
}
