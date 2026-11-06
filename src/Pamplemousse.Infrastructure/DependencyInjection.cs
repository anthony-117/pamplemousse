using System.Net;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Infrastructure.Accounts;
using Pamplemousse.Infrastructure.Assistant;
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
        AddChatModel(services, keys);
        services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>();
        services.AddHttpClient<IExchangeRateProvider, FrankfurterExchangeRateProvider>();
        services.AddHttpClient<IHolidayProvider, NagerHolidayProvider>();
        services.AddHttpClient<IAdvisoryProvider, StateDeptAdvisoryProvider>();
        services.AddHttpClient<IEventProvider, TicketmasterEventProvider>();
        // A slow answer becomes a warning instead of holding up the page.
        services.AddHttpClient<IFlightProvider, TravelpayoutsFlightProvider>(c => c.Timeout = TimeSpan.FromSeconds(5));
        AddBookingComClients(services);
        services.AddHttpClient<OpenTripMapAttractionProvider>();
        services.AddHttpClient<OpenTripMapRestaurantProvider>();
        services.AddHttpClient<OpenTripMapNatureProvider>();
        services.AddHttpClient<OpenTripMapNightlifeProvider>();
        // Places near each hotel for weighted search; a slow answer makes that hotel's interests count as average.
        services.AddHttpClient<INearbyPlaces, OpenTripMapNearbyPlaces>(c => c.Timeout = TimeSpan.FromSeconds(20));
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
        services.AddTransient<IPlaceProvider>(sp => sp.GetRequiredService<OpenTripMapNatureProvider>());
        services.AddTransient<IPlaceProvider>(sp => sp.GetRequiredService<OpenTripMapNightlifeProvider>());

        return builder;
    }

    /// <summary>Hotels and cars share the Booking COM API key and its monthly request count (ADR 0016).</summary>
    private static void AddBookingComClients(IServiceCollection services)
    {
        // A car answer is several MB of JSON. Without Accept-Encoding the source sends it uncompressed, and on a slow day the
        // body was still arriving at the 45 s limit after the headers came at 5-16 s. Compressed, it is a fraction of that.
        static HttpMessageHandler Compressed() => new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All };

        services.AddHttpClient<IHotelProvider, BookingComHotelProvider>(c => c.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(Compressed);

        // A car search takes 20-30 s at the source (research R-7), past the standard handler's 30 s total and 10 s per try,
        // and a retry would cost a second request. So one try, with its own longer limit.
        // RemoveAllResilienceHandlers is marked experimental; it is the documented way to opt one client out of the defaults.
#pragma warning disable EXTEXP0001
        services.AddHttpClient<ICarProvider, BookingComCarProvider>(c => c.Timeout = TimeSpan.FromSeconds(45))
            .ConfigurePrimaryHttpMessageHandler(Compressed)
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
    }

    /// <summary>
    /// Gemini through Google's SDK, wrapped in the tool loop and the error translation (ADR 0018). The translation sits inside
    /// the loop, around each call to Gemini. Without a key, a stand-in answers every message with a warning.
    /// </summary>
    private static void AddChatModel(IServiceCollection services, ExternalApiOptions keys)
    {
        if (keys.GeminiApiKey.Length == 0)
        {
            services.AddSingleton<IChatClient, NotConfiguredChatClient>();
            return;
        }

        // One attempt per call (the SDK's default): a busy model becomes a warning at once, with no hidden retries.
        services.AddSingleton(_ => new Client(
            apiKey: keys.GeminiApiKey,
            httpOptions: new HttpOptions { Timeout = (int)keys.GeminiTimeout.TotalMilliseconds }));
        services.AddChatClient(sp => sp.GetRequiredService<Client>().AsIChatClient(keys.GeminiModel))
            .UseFunctionInvocation(configure: loop => loop.MaximumIterationsPerRequest = keys.AssistantMaxToolRounds)
            .Use(inner => new GeminiErrorTranslator(inner));
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
            services.AddSingleton<FirestoreCarData>();
            services.AddSingleton<ITravelProfileStore, FirestoreTravelProfiles>();
            services.AddSingleton<IPlanStore, FirestorePlanStore>();
            services.AddSingleton<IAccountDeleter>(_ => new FirebaseAccountDeleter(keys.FirebaseProjectId, keys.GoogleCredentialsPath));
            services.AddHostedService<FirestoreWarmUp>();
        }
        else
        {
            services.AddSingleton<InMemoryHotelData>();
            services.AddSingleton<InMemoryCarData>();
            services.AddSingleton<ITravelProfileStore, InMemoryTravelProfiles>();
            services.AddSingleton<IPlanStore, InMemoryPlanStore>();
            services.AddSingleton<IAccountDeleter, NotConfiguredAccountDeleter>();
        }

        services.AddSingleton<IDeletedTravellers, DeletedTravellers>();

        services.AddSingleton(Store<ICityMappingStore>);
        services.AddSingleton(Store<IHotelStore>);
        services.AddSingleton(Store<IPriceHistory>);
        services.AddSingleton(CarStore<ICarAirportStore>);
        services.AddSingleton(CarStore<ICarHistory>);

        T Store<T>(IServiceProvider sp)
            where T : class => keys.FirebaseProjectId.Length > 0
                ? (T)(object)sp.GetRequiredService<FirestoreHotelData>()
                : (T)(object)sp.GetRequiredService<InMemoryHotelData>();

        T CarStore<T>(IServiceProvider sp)
            where T : class => keys.FirebaseProjectId.Length > 0
                ? (T)(object)sp.GetRequiredService<FirestoreCarData>()
                : (T)(object)sp.GetRequiredService<InMemoryCarData>();
    }
}
