using Microsoft.Extensions.Logging.Abstractions;
using Pamplemousse.Api.Features.Destinations;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Events;
using Pamplemousse.Domain.Places;

namespace Pamplemousse.Api.Tests.Destinations;

/// <summary>Unit tests: the service with hand-written fakes, no HTTP host.</summary>
public class DestinationServiceTests
{
    private static readonly Destination Rome = new("3169070", "Rome", "Lazio", "IT", new GeoPoint(41.89, 12.51), 2_318_895, "Europe/Rome");
    private static readonly Country Italy = new("IT", "Italy", "Italian Republic", "Rome", "Europe", null, [new CurrencyInfo("EUR", "Euro", "€")], ["Italian"], "+39", null);
    private static readonly TripDates Dates = new(new DateOnly(2027, 4, 10), new DateOnly(2027, 4, 14));

    private sealed class Fakes : IReferenceData, IWeatherProvider, IExchangeRateProvider, IHolidayProvider, IAdvisoryProvider, IEventProvider
    {
        public ExchangeRate? Rate { get; set; } = new("USD", "EUR", 0.89m, new DateOnly(2027, 4, 1), "test");
        public Exception? WeatherError { get; set; }
        public Exception? EventsError { get; set; }

        public IReadOnlyList<Country> Countries => [Italy];
        public IReadOnlyList<Destination> SearchDestinations(string query, int limit) => [Rome];
        public Destination? FindDestination(string id) => Rome;
        public Country? FindCountry(string code) => string.Equals(code, "IT", StringComparison.Ordinal) ? Italy : null;
        public VisaRequirement? FindVisa(string passportCountry, string destinationCountry) => new(passportCountry, destinationCountry, "visa required", null);

        public Task<WeatherReport> GetAsync(GeoPoint location, DateOnly firstDay, DateOnly lastDay, CancellationToken ct) =>
            WeatherError is null ? Task.FromResult(new WeatherReport(WeatherKind.Forecast, [])) : Task.FromException<WeatherReport>(WeatherError);

        public Task<ExchangeRate?> GetAsync(string baseCurrency, string quoteCurrency, CancellationToken ct) => Task.FromResult(Rate);

        Task<IReadOnlyList<Holiday>> IHolidayProvider.GetAsync(string countryCode, DateOnly firstDay, DateOnly lastDay, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Holiday>>([]);

        public Task<TravelAdvisory?> GetAsync(Country country, CancellationToken ct) => Task.FromResult<TravelAdvisory?>(null);

        public Task<IReadOnlyList<TravelEvent>> SearchAsync(Destination destination, DateOnly firstDay, DateOnly lastDay, int limit, CancellationToken ct) =>
            EventsError is null ? Task.FromResult<IReadOnlyList<TravelEvent>>([]) : Task.FromException<IReadOnlyList<TravelEvent>>(EventsError);
    }

    private sealed class FakePlaces(PlaceCategory category, Exception? error = null) : IPlaceProvider
    {
        public string Source => "otm";
        public PlaceCategory Category => category;
        public int Calls { get; private set; }

        public Task<IReadOnlyList<Place>> SearchAsync(Destination destination, int limit, CancellationToken ct)
        {
            Calls++;
            return error is null
                ? Task.FromResult<IReadOnlyList<Place>>([new Place("otm:1", "Colosseum", category, Rome.Location, null, null, null, null, null, null, null, null, [], "test")])
                : Task.FromException<IReadOnlyList<Place>>(error);
        }

        public Task<Place?> GetAsync(string providerId, CancellationToken ct) =>
            Task.FromResult<Place?>(new Place($"otm:{providerId}", "Colosseum", category, Rome.Location, null, null, null, null, null, null, null, null, [], "test"));
    }

    private static DestinationService Service(Fakes f, params IPlaceProvider[] places) =>
        new(f, f, f, f, f, places.Length > 0 ? places : [new FakePlaces(PlaceCategory.Attraction), new FakePlaces(PlaceCategory.Restaurant)], f, NullLogger<DestinationService>.Instance);

    [Fact]
    public async Task Overview_without_passport_has_no_visa_section()
    {
        var overview = await Service(new Fakes()).GetOverviewAsync(Rome, Dates, passport: null, "USD", CancellationToken.None);

        Assert.Null(overview.Visa);
        Assert.Equal("Italy", overview.Country?.Name);
        Assert.Empty(overview.Warnings);
    }

    [Fact]
    public async Task Missing_rate_for_another_currency_is_explained()
    {
        var overview = await Service(new Fakes { Rate = null }).GetOverviewAsync(Rome, Dates, "LB", "LBP", CancellationToken.None);

        Assert.Null(overview.Exchange);
        Assert.Contains("No exchange rate between LBP and EUR from the European Central Bank.", overview.Warnings, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Failing_source_becomes_a_warning_and_other_sections_still_load()
    {
        var overview = await Service(new Fakes { WeatherError = new HttpRequestException("down") }).GetOverviewAsync(Rome, Dates, "LB", "USD", CancellationToken.None);

        Assert.Null(overview.Weather);
        Assert.NotNull(overview.Exchange);
        Assert.Equal("visa required", overview.Visa?.Requirement);
        Assert.Contains("Weather is unavailable right now.", overview.Warnings, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Places_use_the_provider_for_the_requested_category()
    {
        var attractions = new FakePlaces(PlaceCategory.Attraction);
        var restaurants = new FakePlaces(PlaceCategory.Restaurant);

        var result = await Service(new Fakes(), attractions, restaurants).GetPlacesAsync(Rome, PlaceCategory.Restaurant, 5, CancellationToken.None);

        Assert.Equal(PlaceCategory.Restaurant, result.Items[0].Category);
        Assert.Equal((0, 1), (attractions.Calls, restaurants.Calls));
    }

    [Fact]
    public async Task Provider_without_a_key_gives_its_message_as_warning()
    {
        var missingKey = new ProviderNotConfiguredException("OpenTripMap", "OPENTRIPMAP_API_KEY");

        var result = await Service(new Fakes(), new FakePlaces(PlaceCategory.Attraction, missingKey)).GetPlacesAsync(Rome, PlaceCategory.Attraction, 5, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal([missingKey.Message], result.Warnings, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("nope")]      // no source prefix
    [InlineData("gp:abc")]    // unknown source
    public async Task Unknown_place_ids_return_nothing(string id)
    {
        var (place, warning) = await Service(new Fakes()).GetPlaceAsync(id, CancellationToken.None);

        Assert.Null(place);
        Assert.Null(warning);
    }

    [Fact]
    public async Task Place_id_is_routed_by_its_source_prefix()
    {
        var (place, _) = await Service(new Fakes()).GetPlaceAsync("otm:W3", CancellationToken.None);

        Assert.Equal("otm:W3", place?.Id);
    }

    [Fact]
    public async Task Events_timeout_becomes_a_warning()
    {
        var result = await Service(new Fakes { EventsError = new TaskCanceledException() }).GetEventsAsync(Rome, Dates, 10, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(["Events is unavailable right now."], result.Warnings, StringComparer.Ordinal);
    }
}
