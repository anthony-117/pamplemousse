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
        services.AddHttpClient<OpenTripMapPlaceProvider>();
        services.AddHttpClient<GooglePlacesRestaurantProvider>();
        services.AddTransient<IPlaceProvider>(sp => sp.GetRequiredService<OpenTripMapPlaceProvider>());
        services.AddTransient<IPlaceProvider>(sp => sp.GetRequiredService<GooglePlacesRestaurantProvider>());

        return builder;
    }
}
