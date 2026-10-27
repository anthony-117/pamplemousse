using System.Net;
using Pamplemousse.Infrastructure.Providers;

namespace Pamplemousse.Infrastructure.Tests.Providers;

public class FrankfurterExchangeRateProviderTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Parses_the_latest_rate()
    {
        var http = new StubHttp().On("base=USD&symbols=EUR", """{"amount":1.0,"base":"USD","date":"2026-10-26","rates":{"EUR":0.89381}}""");
        var provider = new FrankfurterExchangeRateProvider(http.Client(), TestDoubles.Cache(), Clock);

        var rate = await provider.GetAsync("usd", "eur", CancellationToken.None);

        Assert.NotNull(rate);
        Assert.Equal(0.89381m, rate.Rate);
        Assert.Equal(new DateOnly(2026, 10, 26), rate.Date);
        Assert.Equal(("USD", "EUR"), (rate.Base, rate.Quote));
    }

    [Fact]
    public async Task Same_currency_is_one_without_calling_the_api()
    {
        var http = new StubHttp();
        var provider = new FrankfurterExchangeRateProvider(http.Client(), TestDoubles.Cache(), Clock);

        var rate = await provider.GetAsync("EUR", "eur", CancellationToken.None);

        Assert.Equal(1m, rate?.Rate);
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Currency_not_published_by_the_ECB_returns_null(HttpStatusCode status)
    {
        var http = new StubHttp().On("symbols=LBP", """{"message":"not found"}""", status);
        var provider = new FrankfurterExchangeRateProvider(http.Client(), TestDoubles.Cache(), Clock);

        Assert.Null(await provider.GetAsync("USD", "LBP", CancellationToken.None));
    }

    [Fact]
    public async Task Server_errors_are_not_hidden()
    {
        var http = new StubHttp().On("frankfurter", "oops", HttpStatusCode.ServiceUnavailable);
        var provider = new FrankfurterExchangeRateProvider(http.Client(), TestDoubles.Cache(), Clock);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetAsync("USD", "EUR", CancellationToken.None));
    }
}
