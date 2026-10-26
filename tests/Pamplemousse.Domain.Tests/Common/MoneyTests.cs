using Pamplemousse.Domain.Common;

namespace Pamplemousse.Domain.Tests.Common;

public class MoneyTests
{
    [Fact]
    public void Currency_is_normalised_to_upper_case()
    {
        var money = new Money(289m, "usd");

        Assert.Equal("USD", money.Currency);
        Assert.Equal("289.00 USD", money.ToString());
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDT")]
    [InlineData("U$D")]
    public void Invalid_currency_codes_are_rejected(string currency)
    {
        Assert.Throws<ArgumentException>(() => new Money(10m, currency));
    }

    [Fact]
    public void Negative_amounts_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(-1m, "EUR"));
    }

    [Fact]
    public void Adding_same_currency_sums_amounts()
    {
        var total = new Money(289m, "USD").Add(new Money(612m, "USD"));

        Assert.Equal(new Money(901m, "USD"), total);
    }

    [Fact]
    public void Adding_different_currencies_throws()
    {
        Assert.Throws<InvalidOperationException>(() => new Money(1m, "USD").Add(new Money(1m, "EUR")));
    }
}
