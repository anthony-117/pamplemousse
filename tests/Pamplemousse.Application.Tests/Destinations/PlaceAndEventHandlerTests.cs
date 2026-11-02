using Microsoft.Extensions.Logging.Abstractions;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Destinations.Queries.GetDestination;
using Pamplemousse.Application.Destinations.Queries.GetPlace;
using Pamplemousse.Application.Destinations.Queries.ListCountries;
using Pamplemousse.Application.Destinations.Queries.ListEvents;
using Pamplemousse.Application.Destinations.Queries.ListPlaces;
using Pamplemousse.Application.Destinations.Queries.SearchDestinations;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Places;

namespace Pamplemousse.Application.Tests.Destinations;

public class PlaceAndEventHandlerTests
{
    private static ListPlacesHandler ListPlaces(params IPlaceProvider[] providers) => new(new Fakes(), providers, NullLogger<ListPlacesHandler>.Instance);

    private static GetPlaceHandler GetPlace() => new([new FakePlaces(PlaceCategory.Attraction)], NullLogger<GetPlaceHandler>.Instance);

    [Fact]
    public async Task Places_use_the_provider_for_the_requested_category()
    {
        var attractions = new FakePlaces(PlaceCategory.Attraction);
        var restaurants = new FakePlaces(PlaceCategory.Restaurant);

        var result = await ListPlaces(attractions, restaurants).HandleAsync(new ListPlacesQuery(Fakes.Rome.Id, "RESTAURANT", 5), CancellationToken.None);

        Assert.Equal(PlaceCategory.Restaurant, result.Items[0].Category);
        Assert.Equal((0, 1), (attractions.Calls, restaurants.Calls));
    }

    [Fact]
    public async Task Places_default_to_attractions()
    {
        var result = await ListPlaces(new FakePlaces(PlaceCategory.Attraction)).HandleAsync(new ListPlacesQuery(Fakes.Rome.Id, null, null), CancellationToken.None);

        Assert.Equal(PlaceCategory.Attraction, result.Category);
    }

    [Theory]
    [InlineData("museum")]
    [InlineData("7")] // Enum.TryParse accepts numbers
    public async Task Unknown_category_is_rejected(string category)
    {
        var error = await Assert.ThrowsAsync<DomainException>(() =>
            ListPlaces(new FakePlaces(PlaceCategory.Attraction)).HandleAsync(new ListPlacesQuery(Fakes.Rome.Id, category, null), CancellationToken.None));

        Assert.Equal("category", error.Field);
    }

    [Fact]
    public async Task Provider_without_a_key_gives_its_message_as_warning()
    {
        var missingKey = new ProviderNotConfiguredException("OpenTripMap", "OPENTRIPMAP_API_KEY");

        var result = await ListPlaces(new FakePlaces(PlaceCategory.Attraction, missingKey)).HandleAsync(new ListPlacesQuery(Fakes.Rome.Id, null, 5), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal([missingKey.Message], result.Warnings, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("nope")]      // no source prefix
    [InlineData("gp:abc")]    // unknown source
    public async Task Unknown_place_ids_return_nothing(string id)
    {
        var result = await GetPlace().HandleAsync(new GetPlaceQuery(id), CancellationToken.None);

        Assert.Null(result.Place);
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task Place_id_is_routed_by_its_source_prefix()
    {
        var result = await GetPlace().HandleAsync(new GetPlaceQuery("otm:W3"), CancellationToken.None);

        Assert.Equal("otm:W3", result.Place?.Id);
    }

    [Fact]
    public async Task Events_timeout_becomes_a_warning()
    {
        var fakes = new Fakes { EventsError = new TaskCanceledException() };
        var handler = new ListEventsHandler(fakes, fakes, new FixedClock(), NullLogger<ListEventsHandler>.Instance);

        var result = await handler.HandleAsync(new ListEventsQuery(Fakes.Rome.Id, null, null, 10), CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(["Events is unavailable right now."], result.Warnings, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("r")]
    [InlineData("  r  ")]
    public async Task Search_needs_at_least_two_characters(string? term)
    {
        Assert.Empty(await new SearchDestinationsHandler(new Fakes()).HandleAsync(new SearchDestinationsQuery(term, null), CancellationToken.None));
    }

    [Fact]
    public async Task Search_adds_the_country_name()
    {
        var result = Assert.Single(await new SearchDestinationsHandler(new Fakes()).HandleAsync(new SearchDestinationsQuery("rom", null), CancellationToken.None));

        Assert.Equal("Italy", result.CountryName);
    }

    [Fact]
    public async Task Unknown_destination_details_are_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => new GetDestinationHandler(new Fakes()).HandleAsync(new GetDestinationQuery("nope"), CancellationToken.None));
    }

    [Fact]
    public async Task Countries_list_their_currency_codes()
    {
        var country = Assert.Single(await new ListCountriesHandler(new Fakes()).HandleAsync(new ListCountriesQuery(), CancellationToken.None));

        Assert.Equal(["EUR"], country.Currencies, StringComparer.Ordinal);
    }
}
