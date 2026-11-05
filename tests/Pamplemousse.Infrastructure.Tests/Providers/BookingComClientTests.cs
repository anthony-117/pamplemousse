using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pamplemousse.Application.Abstractions;

namespace Pamplemousse.Infrastructure.Tests.Providers;

public sealed class BookingComClientTests
{
    /// <summary>
    /// A car answer is several MB of JSON. Sent uncompressed, it was still arriving at the 45 s limit on a slow day (sprint 2
    /// review), so both Booking COM clients must ask for gzip, deflate or brotli.
    /// </summary>
    [Theory]
    [InlineData(nameof(ICarProvider))]
    [InlineData(nameof(IHotelProvider))]
    public void Booking_com_clients_ask_for_compressed_answers(string client)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.AddInfrastructure(_ => { });
        using var services = builder.Services.BuildServiceProvider();

        var handler = services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(client);
        while (handler is DelegatingHandler outer)
        {
            handler = outer.InnerHandler!;
        }

        var primary = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.Equal(DecompressionMethods.All, primary.AutomaticDecompression);
    }
}
