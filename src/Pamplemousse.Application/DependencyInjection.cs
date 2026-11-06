using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Application.Assistant;
using Pamplemousse.Application.Assistant.Queries.AskAssistant;
using Pamplemousse.Application.Cars;
using Pamplemousse.Application.Cars.Queries.GetCarPlaces;
using Pamplemousse.Application.Cars.Queries.SearchCars;
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
using Pamplemousse.Application.Itinerary;
using Pamplemousse.Application.Ranking;
using Pamplemousse.Application.Travellers;
using Pamplemousse.Domain.Itinerary;

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
        services.AddScoped<IQueryHandler<GetCarPlacesQuery, GetCarPlacesResponse>, GetCarPlacesHandler>();
        services.AddScoped<IQueryHandler<SearchCarsQuery, SearchCarsResponse>, SearchCarsHandler>();
        services.AddScoped<HotelNearby>();
        services.AddScoped<AssistantTools>();
        services.AddScoped<IQueryHandler<AskAssistantQuery, AskAssistantResponse>, AskAssistantHandler>();
        services.AddScoped<Profiles>();
        services.AddScoped<IQueryHandler<GetProfileQuery, ProfileResponse>, GetProfileHandler>();
        services.AddScoped<ICommandHandler<UpdateProfileCommand, ProfileResponse>, UpdateProfileHandler>();
        services.AddScoped<ICommandHandler<DeleteAccountCommand, bool>, DeleteAccountHandler>();
        services.AddScoped<PlanInputs>();
        services.AddScoped<ICommandHandler<BuildPlanCommand, Plan>, BuildPlanHandler>();
        services.AddScoped<IQueryHandler<GetPlanQuery, Plan>, GetPlanHandler>();
        services.AddScoped<IQueryHandler<ListPlansQuery, PlanList>, ListPlansHandler>();
        services.AddScoped<ICommandHandler<ChangePlanCommand, Plan>, ChangePlanHandler>();
        services.AddScoped<ICommandHandler<DeletePlanCommand, bool>, DeletePlanHandler>();
        services.AddScoped<IQueryHandler<GetAlternativesQuery, Plan>, GetAlternativesHandler>();
        services.AddScoped<ICommandHandler<SwapOfferCommand, Plan>, SwapOfferHandler>();

        // The API registers the configured settings first; anything else (tests, the bots) gets the free-plan defaults.
        services.TryAddSingleton(new HotelSettings());
        services.TryAddSingleton(new CarSettings());
        return services;
    }
}
