using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Infrastructure.Caching;
using Pamplemousse.Infrastructure.Providers;
using Pamplemousse.Infrastructure.ReferenceData;
using Pamplemousse.Infrastructure.Storage;
using StackExchange.Redis;

namespace Pamplemousse.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers reference data, the cache and every external provider.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder, Action<ExternalApiOptions> configureKeys)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.Configure(configureKeys);
        var keys = new ExternalApiOptions();
        configureKeys(keys);
        services.AddSingleton<IReferenceData, ReferenceDataStore>();
        services.AddSingleton<FlightCityStore>();
        services.AddSingleton<IFlightCities>(sp => sp.GetRequiredService<FlightCityStore>());

        // Redis when Aspire (or the host) provides a "cache" connection string, in-memory otherwise.
        // The Redis cache also registers the connection itself, which the lock and the request counter use.
        if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("cache")))
        {
            builder.AddRedisDistributedCache("cache");
            services.AddSingleton<ISearchLock>(sp => new RedisSearchLock(sp.GetRequiredService<IConnectionMultiplexer>()));
            services.AddSingleton<IRequestQuota>(sp => new RedisRequestQuota(sp.GetRequiredService<IConnectionMultiplexer>(), sp.GetRequiredService<TimeProvider>()));
        }
        else
        {
            services.AddDistributedMemoryCache();
            services.AddSingleton<ISearchLock, InMemorySearchLock>();
            services.AddSingleton<IRequestQuota, InMemoryRequestQuota>();
        }

        services.AddSingleton<JsonCache>();
        services.AddSingleton<ISearchCache, DistributedSearchCache>();
        AddHotelStorage(services, keys);
        services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>();
        services.AddHttpClient<IExchangeRateProvider, FrankfurterExchangeRateProvider>();
        services.AddHttpClient<IHolidayProvider, NagerHolidayProvider>();
        services.AddHttpClient<IAdvisoryProvider, StateDeptAdvisoryProvider>();
        services.AddHttpClient<IEventProvider, TicketmasterEventProvider>();
        // A slow answer becomes a warning instead of holding up the page.
        services.AddHttpClient<IFlightProvider, TravelpayoutsFlightProvider>(c => c.Timeout = TimeSpan.FromSeconds(5));
        services.AddHttpClient<IHotelProvider, BookingComHotelProvider>(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient<OpenTripMapAttractionProvider>();
        services.AddHttpClient<OpenTripMapRestaurantProvider>();
        services.AddHttpClient<WikidataAttractionProvider>(c =>
        {
            // Wikimedia asks API clients to identify themselves. A slow query falls back to OpenTripMap instead of hanging the page.
            c.DefaultRequestHeaders.UserAgent.ParseAdd("TravelGenie/0.1 (https://github.com/anthony-117/pamplemousse)");
            c.Timeout = TimeSpan.FromSeconds(12);
        });

        // The first provider of a category serves its list; every provider serves details for its own ids.
        services.AddTransient<IPlaceProvider>(sp => sp.GetRequiredService<WikidataAttractionProvider>());
        services.AddTransient<IPlaceProvider>(sp => sp.GetRequiredService<OpenTripMapAttractionProvider>());
        services.AddTransient<IPlaceProvider>(sp => sp.GetRequiredService<OpenTripMapRestaurantProvider>());

        return builder;
    }

    /// <summary>Firestore when a Firebase project is set (ADR 0017); otherwise memory, with a warning in the log.</summary>
    private static void AddHotelStorage(IServiceCollection services, ExternalApiOptions keys)
    {
        if (keys.FirebaseProjectId.Length > 0)
        {
            services.AddSingleton(_ => new FirestoreDbBuilder
            {
                ProjectId = keys.FirebaseProjectId,
                Credential = keys.GoogleCredentialsPath.Length > 0
                    ? CredentialFactory.FromFile<ServiceAccountCredential>(keys.GoogleCredentialsPath).ToGoogleCredential()
                    : null,

                // FIRESTORE_EMULATOR_HOST, when set, points everything at the local emulator instead.
                EmulatorDetection = Google.Api.Gax.EmulatorDetection.EmulatorOrProduction,
            }.Build());
            services.AddSingleton<FirestoreHotelData>();
        }
        else
        {
            services.AddSingleton<InMemoryHotelData>();
        }

        services.AddSingleton(Store<ICityMappingStore>);
        services.AddSingleton(Store<IHotelStore>);
        services.AddSingleton(Store<IPriceHistory>);

        T Store<T>(IServiceProvider sp)
            where T : class => keys.FirebaseProjectId.Length > 0
                ? (T)(object)sp.GetRequiredService<FirestoreHotelData>()
                : (T)(object)sp.GetRequiredService<InMemoryHotelData>();
    }
}
