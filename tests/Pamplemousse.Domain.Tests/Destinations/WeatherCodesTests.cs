using Pamplemousse.Domain.Destinations;

namespace Pamplemousse.Domain.Tests.Destinations;

public class WeatherCodesTests
{
    [Theory]
    [InlineData(0, "Clear sky")]
    [InlineData(1, "Mainly clear")]
    [InlineData(63, "Rain")]
    [InlineData(95, "Thunderstorm")]
    [InlineData(1234, "Unknown")]
    public void Describe_maps_wmo_codes(int code, string expected)
    {
        Assert.Equal(expected, WeatherCodes.Describe(code));
    }
}
