using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Destinations;
using Pamplemousse.Application.Destinations.Queries.GetDestination;
using Pamplemousse.Application.Destinations.Queries.GetOverview;
using Pamplemousse.Application.Destinations.Queries.GetPlace;
using Pamplemousse.Application.Destinations.Queries.ListCountries;
using Pamplemousse.Application.Destinations.Queries.ListEvents;
using Pamplemousse.Application.Destinations.Queries.ListPlaces;
using Pamplemousse.Application.Destinations.Queries.SearchDestinations;
using Pamplemousse.Application.Flights.Queries.SearchFlights;
using Pamplemousse.Application.Hotels;
using Pamplemousse.Application.Hotels.Queries.GetHotel;
using Pamplemousse.Application.Hotels.Queries.SearchHotels;

namespace Pamplemousse.Application;

public static class DependencyInjection
{
    /// <summary>Registers every use case. A handler missing here fails the endpoint tests.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<SearchDestinationsQuery, IReadOnlyList<DestinationSummary>>, SearchDestinationsHandler>();
        services.AddScoped<IQueryHandler<GetDestinationQuery, DestinationDetailsResponse>, GetDestinationHandler>();
        services.AddScoped<IQueryHandler<GetOverviewQuery, OverviewResponse>, GetOverviewHandler>();
        services.AddScoped<IQueryHandler<ListPlacesQuery, PlacesResponse>, ListPlacesHandler>();
        services.AddScoped<IQueryHandler<GetPlaceQuery, PlaceDetailsResponse>, GetPlaceHandler>();
        services.AddScoped<IQueryHandler<ListEventsQuery, EventsResponse>, ListEventsHandler>();
        services.AddScoped<IQueryHandler<ListCountriesQuery, IReadOnlyList<CountryOption>>, ListCountriesHandler>();
        services.AddScoped<IQueryHandler<SearchFlightsQuery, SearchFlightsResponse>, SearchFlightsHandler>();
        services.AddScoped<IQueryHandler<SearchHotelsQuery, SearchHotelsResponse>, SearchHotelsHandler>();
        services.AddScoped<IQueryHandler<GetHotelQuery, GetHotelResponse>, GetHotelHandler>();

        // The API registers the configured settings first; anything else (tests, the bots) gets the free-plan defaults.
        services.TryAddSingleton(new HotelSettings());
        return services;
    }
}
