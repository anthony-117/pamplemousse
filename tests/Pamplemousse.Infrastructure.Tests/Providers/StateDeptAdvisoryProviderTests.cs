using Pamplemousse.Domain.Destinations;
using Pamplemousse.Infrastructure.Providers;

namespace Pamplemousse.Infrastructure.Tests.Providers;

public class StateDeptAdvisoryProviderTests
{
    private static readonly string Feed = $$"""
        [{"Title":"Italy - Level 2: Exercise Increased Caution","Link":"https://travel.state.gov/italy",
          "Summary":"<p>Exercise increased caution in <b>Italy</b> due to terrorism .</p>"},
         {"Title":"Lebanon - Level 4: Do Not Travel","Link":"https://travel.state.gov/lebanon",
          "Summary":"<p>{{new string('x', 400)}}</p>"},
         {"Title":"Not an advisory title","Link":"x","Summary":""}]
        """;

    private static Country Country(string name, string official) => new("XX", name, official, null, null, null, [], [], null, null);

    private static StateDeptAdvisoryProvider Provider(StubHttp http) => new(http.Client(), TestDoubles.Cache());

    [Fact]
    public async Task Parses_level_label_and_plain_text_summary()
    {
        var advisory = await Provider(new StubHttp().On("TravelAdvisories", Feed)).GetAsync(Country("Italy", "Italian Republic"), CancellationToken.None);

        Assert.NotNull(advisory);
        Assert.Equal(2, advisory.Level);
        Assert.Equal("Exercise Increased Caution", advisory.Label);
        Assert.Equal("Exercise increased caution in Italy due to terrorism.", advisory.Summary);
        Assert.Equal("https://travel.state.gov/italy", advisory.SourceUrl);
    }

    [Fact]
    public async Task Long_summaries_are_shortened()
    {
        var advisory = await Provider(new StubHttp().On("TravelAdvisories", Feed)).GetAsync(Country("Lebanon", "Lebanese Republic"), CancellationToken.None);

        Assert.Equal(4, advisory?.Level);
        Assert.True(advisory!.Summary.Length <= 281);
        Assert.EndsWith("…", advisory.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Matches_the_official_name_too()
    {
        var advisory = await Provider(new StubHttp().On("TravelAdvisories", Feed)).GetAsync(Country("Italia", "Italy"), CancellationToken.None);

        Assert.Equal(2, advisory?.Level);
    }

    [Fact]
    public async Task Unknown_country_has_no_advisory_and_the_feed_is_fetched_once()
    {
        var http = new StubHttp().On("TravelAdvisories", Feed);
        var provider = Provider(http);

        Assert.Null(await provider.GetAsync(Country("Atlantis", "Atlantis"), CancellationToken.None));
        Assert.NotNull(await provider.GetAsync(Country("Italy", "Italian Republic"), CancellationToken.None));
        Assert.Single(http.Requests);
    }
}
