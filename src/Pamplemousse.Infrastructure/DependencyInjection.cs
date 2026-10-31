using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pamplemousse.Domain.Abstractions;
using Pamplemousse.Infrastructure.Caching;
using Pamplemousse.Infrastructure.Providers;
using Pamplemousse.Infrastructure.ReferenceData;

namespace Pamplemousse.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers reference data, the cache and every external provider.</summary>
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder, Action<ExternalApiOptions> configureKeys)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        services.Configure(configureKeys);
        services.AddSingleton<IReferenceData, ReferenceDataStore>();

        // Redis when Aspire (or the host) provides a "cache" connection string, in-memory otherwise.
        if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("cache")))
        {
            builder.AddRedisDistributedCache("cache");
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddSingleton<JsonCache>();
        services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>();
        services.AddHttpClient<IExchangeRateProvider, FrankfurterExchangeRateProvider>();
        services.AddHttpClient<IHolidayProvider, NagerHolidayProvider>();
        services.AddHttpClient<IAdvisoryProvider, StateDeptAdvisoryProvider>();
        services.AddHttpClient<IEventProvider, TicketmasterEventProvider>();
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
}
