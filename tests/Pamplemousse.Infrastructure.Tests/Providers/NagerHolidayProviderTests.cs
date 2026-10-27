using System.Net;
using Pamplemousse.Infrastructure.Providers;

namespace Pamplemousse.Infrastructure.Tests.Providers;

public class NagerHolidayProviderTests
{
    private const string Italy2027 = """
        [{"date":"2027-04-05","localName":"Lunedì dell'Angelo","name":"Easter Monday","global":true},
         {"date":"2027-04-25","localName":"Festa della Liberazione","name":"Liberation Day","global":true},
         {"date":"2027-06-24","localName":"San Giovanni","name":"St. John's Day","global":false}]
        """;

    [Fact]
    public async Task Returns_nationwide_holidays_inside_the_dates_only()
    {
        var http = new StubHttp().On("PublicHolidays/2027/IT", Italy2027);
        var provider = new NagerHolidayProvider(http.Client(), TestDoubles.Cache());

        var holidays = await provider.GetAsync("it", new DateOnly(2027, 4, 20), new DateOnly(2027, 6, 30), CancellationToken.None);

        var holiday = Assert.Single(holidays); // Easter Monday is before the trip, St. John's Day is regional
        Assert.Equal("Liberation Day", holiday.Name);
        Assert.Equal("Festa della Liberazione", holiday.LocalName);
    }

    [Fact]
    public async Task Trip_across_new_year_asks_for_both_years()
    {
        var http = new StubHttp()
            .On("PublicHolidays/2026/IT", """[{"date":"2026-12-31","localName":"x","name":"Eve","global":true}]""")
            .On("PublicHolidays/2027/IT", """[{"date":"2027-01-01","localName":"Capodanno","name":"New Year's Day","global":true}]""");
        var provider = new NagerHolidayProvider(http.Client(), TestDoubles.Cache());

        var holidays = await provider.GetAsync("IT", new DateOnly(2026, 12, 30), new DateOnly(2027, 1, 2), CancellationToken.None);

        Assert.Equal(2, http.Requests.Count);
        Assert.Equal(["Eve", "New Year's Day"], holidays.Select(h => h.Name), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Country_not_covered_returns_no_holidays(HttpStatusCode status)
    {
        var http = new StubHttp().On("PublicHolidays", "", status);
        var provider = new NagerHolidayProvider(http.Client(), TestDoubles.Cache());

        Assert.Empty(await provider.GetAsync("LB", new DateOnly(2027, 4, 1), new DateOnly(2027, 4, 5), CancellationToken.None));
    }
}
