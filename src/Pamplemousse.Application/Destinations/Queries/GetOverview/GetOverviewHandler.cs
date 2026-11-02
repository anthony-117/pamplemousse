using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Common;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Application.Destinations.Queries.GetOverview;

public sealed record GetOverviewQuery(string DestinationId, string? Passport, string? Currency, DateOnly? From, DateOnly? To);

/// <summary>
/// Everything about a city for given dates. Each section is independent: when a source fails or is not configured
/// the section is null and <see cref="Warnings"/> says why, so the page still loads.
/// </summary>
public sealed record OverviewResponse(
    DestinationSummary Destination,
    Country? Country,
    DateRange Dates,
    WeatherReport? Weather,
    ExchangeRate? Exchange,
    IReadOnlyList<Holiday>? Holidays,
    VisaRequirement? Visa,
    TravelAdvisory? Advisory,
    IReadOnlyList<string> Warnings);

public sealed class GetOverviewHandler(
    IReferenceData referenceData,
    IWeatherProvider weather,
    IExchangeRateProvider exchange,
    IHolidayProvider holidays,
    IAdvisoryProvider advisories,
    TimeProvider clock,
    ILogger<GetOverviewHandler> logger) : IQueryHandler<GetOverviewQuery, OverviewResponse>
{
    private const string DefaultCurrency = "USD";

    public async Task<OverviewResponse> HandleAsync(GetOverviewQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var destination = referenceData.FindDestination(query.DestinationId) ?? throw new NotFoundException($"Destination '{query.DestinationId}' was not found.");
        var dates = DateRange.ForTrip(query.From, query.To, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        var currency = string.IsNullOrWhiteSpace(query.Currency) ? DefaultCurrency : query.Currency;

        var sections = new SectionLoader(logger);
        var country = referenceData.FindCountry(destination.CountryCode);
        var localCurrency = country?.Currencies is [var first, ..] ? first.Code : null;

        var weatherTask = sections.LoadAsync("Weather", () => weather.GetAsync(destination.Location, dates.From, dates.To, ct));
        var exchangeTask = localCurrency is null
            ? Task.FromResult<ExchangeRate?>(null)
            : sections.LoadAsync("Exchange rate", () => exchange.GetAsync(currency, localCurrency, ct));
        var holidaysTask = sections.LoadAsync("Public holidays", () => holidays.GetAsync(destination.CountryCode, dates.From, dates.To, ct));
        var advisoryTask = country is null
            ? Task.FromResult<TravelAdvisory?>(null)
            : sections.LoadAsync("Travel advisory", () => advisories.GetAsync(country, ct));

        await Task.WhenAll(weatherTask, exchangeTask, holidaysTask, advisoryTask);

        var rate = await exchangeTask;
        if (rate is null && localCurrency is not null && !string.Equals(localCurrency, currency, StringComparison.OrdinalIgnoreCase))
        {
            sections.AddWarning($"No exchange rate between {currency.ToUpperInvariant()} and {localCurrency} from the European Central Bank.");
        }

        var visa = string.IsNullOrWhiteSpace(query.Passport) ? null : referenceData.FindVisa(query.Passport, destination.CountryCode);

        return new OverviewResponse(
            DestinationSummary.From(destination, country),
            country,
            dates,
            await weatherTask,
            rate,
            await holidaysTask,
            visa,
            await advisoryTask,
            sections.Warnings);
    }
}
