using Pamplemousse.Domain.Destinations;

namespace Pamplemousse.Domain.Tests.Destinations;

public class WeatherCodesTests
{
    [Theory]
    [InlineData(0, "Clear sky")]
    [InlineData(1, "Mainly clear")]
    [InlineData(2, "Partly cloudy")]
    [InlineData(3, "Overcast")]
    [InlineData(45, "Fog")]
    [InlineData(48, "Fog")]
    [InlineData(53, "Drizzle")]
    [InlineData(57, "Freezing drizzle")]
    [InlineData(61, "Light rain")]
    [InlineData(63, "Rain")]
    [InlineData(65, "Heavy rain")]
    [InlineData(66, "Freezing rain")]
    [InlineData(71, "Light snow")]
    [InlineData(73, "Snow")]
    [InlineData(75, "Heavy snow")]
    [InlineData(77, "Snow grains")]
    [InlineData(81, "Rain showers")]
    [InlineData(86, "Snow showers")]
    [InlineData(95, "Thunderstorm")]
    [InlineData(99, "Thunderstorm with hail")]
    [InlineData(1234, "Unknown")]
    public void Describe_maps_every_wmo_code_group(int code, string expected)
    {
        Assert.Equal(expected, WeatherCodes.Describe(code));
    }
}
