using System.Text.Json;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Places;

namespace Pamplemousse.Api.Features.Destinations;

/// <summary>Builds destination pages from the providers; one failing source never breaks the whole response.</summary>
public sealed partial class DestinationService(
    IReferenceData referenceData,
    IWeatherProvider weather,
    IExchangeRateProvider exchange,
    IHolidayProvider holidays,
    IAdvisoryProvider advisories,
    IEnumerable<IPlaceProvider> placeProviders,
    IEventProvider events,
    ILogger<DestinationService> logger)
{
    public async Task<OverviewResponse> GetOverviewAsync(Destination destination, TripDates dates, string? passport, string currency, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(dates);

        var warnings = new List<string>();
        var country = referenceData.FindCountry(destination.CountryCode);
        var localCurrency = country?.Currencies is [var first, ..] ? first.Code : null;

        var weatherTask = Section("Weather", warnings, () => weather.GetAsync(destination.Location, dates.From, dates.To, ct));
        var exchangeTask = localCurrency is null
            ? Task.FromResult<ExchangeRate?>(null)
            : Section("Exchange rate", warnings, () => exchange.GetAsync(currency, localCurrency, ct));
        var holidaysTask = Section("Public holidays", warnings, () => holidays.GetAsync(destination.CountryCode, dates.From, dates.To, ct));
        var advisoryTask = country is null
            ? Task.FromResult<TravelAdvisory?>(null)
            : Section("Travel advisory", warnings, () => advisories.GetAsync(country, ct));

        await Task.WhenAll(weatherTask, exchangeTask, holidaysTask, advisoryTask);

        var rate = await exchangeTask;
        if (rate is null && localCurrency is not null && !string.Equals(localCurrency, currency, StringComparison.OrdinalIgnoreCase))
        {
            AddWarning(warnings, $"No exchange rate between {currency.ToUpperInvariant()} and {localCurrency} from the European Central Bank.");
        }

        var visa = string.IsNullOrWhiteSpace(passport) ? null : referenceData.FindVisa(passport, destination.CountryCode);

        return new OverviewResponse(
            DestinationSummary.From(destination, country),
            country,
            dates,
            await weatherTask,
            rate,
            await holidaysTask,
            visa,
            await advisoryTask,
            warnings);
    }

    public async Task<PlacesResponse> GetPlacesAsync(Destination destination, PlaceCategory category, int limit, CancellationToken ct)
    {
        var warnings = new List<string>();
        var provider = placeProviders.First(p => p.Category == category);
        var items = await Section(category == PlaceCategory.Attraction ? "Attractions" : "Restaurants", warnings,
            () => provider.SearchAsync(destination, limit, ct));
        return new PlacesResponse(category, items ?? [], warnings);
    }

    /// <summary>Looks up a place by its "{source}:{providerId}" id.</summary>
    public async Task<(Place? Place, string? Warning)> GetPlaceAsync(string id, CancellationToken ct)
    {
        var separator = id.IndexOf(':', StringComparison.Ordinal);
        var provider = separator > 0 ? placeProviders.FirstOrDefault(p => string.Equals(p.Source, id[..separator], StringComparison.Ordinal)) : null;
        if (provider is null)
        {
            return (null, null);
        }

        var warnings = new List<string>();
        var place = await Section("Place details", warnings, () => provider.GetAsync(id[(separator + 1)..], ct));
        return (place, warnings.FirstOrDefault());
    }

    public async Task<EventsResponse> GetEventsAsync(Destination destination, TripDates dates, int limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dates);
        var warnings = new List<string>();
        var items = await Section("Events", warnings, () => events.SearchAsync(destination, dates.From, dates.To, limit, ct));
        return new EventsResponse(dates, items ?? [], warnings);
    }

    /// <summary>Runs one section; on failure records a warning and returns default instead of throwing.</summary>
    private async Task<T?> Section<T>(string name, List<string> warnings, Func<Task<T>> load)
    {
        try
        {
            return await load();
        }
        catch (ProviderNotConfiguredException ex)
        {
            AddWarning(warnings, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            LogSectionFailed(logger, name, ex);
            AddWarning(warnings, $"{name} is unavailable right now.");
        }

        return default;
    }

    private static void AddWarning(List<string> warnings, string message)
    {
        lock (warnings)
        {
            warnings.Add(message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Section} failed")]
    private static partial void LogSectionFailed(ILogger logger, string section, Exception exception);
}
