using Pamplemousse.Infrastructure.ReferenceData;

namespace Pamplemousse.Infrastructure.Tests.ReferenceData;

public class ReferenceDataStoreTests
{
    private static readonly ReferenceDataStore Store = new();

    [Fact]
    public void Visa_with_a_number_means_visa_free_days()
    {
        var visa = Store.FindVisa("LB", "BB");

        Assert.NotNull(visa);
        Assert.Equal("visa free", visa.Requirement);
        Assert.Equal(90, visa.VisaFreeDays);
    }

    [Fact]
    public void Visa_for_own_country()
    {
        Assert.Equal("own country", Store.FindVisa("lb", "LB")?.Requirement);
    }

    [Fact]
    public void Country_has_currency_and_calling_code()
    {
        var italy = Store.FindCountry("it");

        Assert.NotNull(italy);
        Assert.Equal("EUR", italy.Currencies[0].Code);
        Assert.Equal("+39", italy.CallingCode);
    }

    [Theory]
    [InlineData("São Paulo", "sao paulo")]
    [InlineData("  ZÜRICH ", "zurich")]
    public void Normalize_removes_case_and_accents(string input, string expected)
    {
        Assert.Equal(expected, ReferenceDataStore.Normalize(input));
    }
}
