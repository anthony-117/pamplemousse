using Microsoft.Extensions.Logging.Abstractions;
using Pamplemousse.Application.Destinations.Queries.GetOverview;
using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Application.Tests.Destinations;

public class GetOverviewHandlerTests
{
    private static readonly DateOnly From = new(2027, 4, 10);
    private static readonly DateOnly To = new(2027, 4, 14);

    private static GetOverviewHandler Handler(Fakes f) => new(f, f, f, f, f, new FixedClock(), NullLogger<GetOverviewHandler>.Instance);

    [Fact]
    public async Task Overview_without_passport_has_no_visa_section()
    {
        var overview = await Handler(new Fakes()).HandleAsync(new GetOverviewQuery(Fakes.Rome.Id, null, "USD", From, To), CancellationToken.None);

        Assert.Null(overview.Visa);
        Assert.Equal("Italy", overview.Country?.Name);
        Assert.Empty(overview.Warnings);
    }

    [Fact]
    public async Task Missing_rate_for_another_currency_is_explained()
    {
        var overview = await Handler(new Fakes { Rate = null }).HandleAsync(new GetOverviewQuery(Fakes.Rome.Id, "LB", "LBP", From, To), CancellationToken.None);

        Assert.Null(overview.Exchange);
        Assert.Contains("No exchange rate between LBP and EUR from the European Central Bank.", overview.Warnings, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Failing_source_becomes_a_warning_and_other_sections_still_load()
    {
        var fakes = new Fakes { WeatherError = new HttpRequestException("down") };

        var overview = await Handler(fakes).HandleAsync(new GetOverviewQuery(Fakes.Rome.Id, "LB", "USD", From, To), CancellationToken.None);

        Assert.Null(overview.Weather);
        Assert.NotNull(overview.Exchange);
        Assert.Equal("visa required", overview.Visa?.Requirement);
        Assert.Contains("Weather is unavailable right now.", overview.Warnings, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Dates_default_from_the_clock()
    {
        var overview = await Handler(new Fakes()).HandleAsync(new GetOverviewQuery(Fakes.Rome.Id, null, null, null, null), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 11, 26), overview.Dates.From);
    }

    [Fact]
    public async Task Unknown_destination_is_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Handler(new Fakes()).HandleAsync(new GetOverviewQuery("nope", null, null, From, To), CancellationToken.None));
    }

    [Fact]
    public async Task Invalid_dates_are_rejected()
    {
        var error = await Assert.ThrowsAsync<DomainException>(() => Handler(new Fakes()).HandleAsync(new GetOverviewQuery(Fakes.Rome.Id, null, null, To, From), CancellationToken.None));

        Assert.Equal("to", error.Field);
    }
}
