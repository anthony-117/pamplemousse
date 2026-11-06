using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Ranking;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Domain.Tests.Travellers;

public class TravelProfileTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddHours(2);
    private static readonly TravellerId Fares = new("uid-fares");

    private static readonly ProfileLookups Known = new(
        c => c is "LB" or "IT",
        c => c is "USD" or "EUR",
        c => c is "276781" or "3169070");

    private static ProfileChanges Changes(
        string? name = "Fares Haddad",
        string? passport = "lb",
        string? currency = "eur",
        string? city = "276781",
        int? budget = 90,
        int? culture = 90) => new(name, passport, currency, city, budget, culture, 50, 50, 20, 50);

    [Fact]
    public void A_first_sign_in_gets_the_defaults_and_the_sign_in_name()
    {
        var profile = TravelProfile.New(Fares, "  Fares Haddad ", "fares@example.com", Created);

        Assert.Equal(("Fares Haddad", "fares@example.com", "LB", "USD", null), (profile.DisplayName, profile.Email, profile.Passport, profile.HomeCurrency, profile.HomeCityId));
        Assert.Equal(Weights.Neutral, profile.Preferences);
        Assert.Equal((Created, Created), (profile.CreatedAt, profile.UpdatedAt));
    }

    [Theory]
    [InlineData(null, "maya.k@example.com", "maya.k")]
    [InlineData("   ", "maya@example.com", "maya")]
    [InlineData(null, null, "Traveller")]
    [InlineData(null, "@example.com", "Traveller")]
    public void Without_a_name_the_email_or_a_plain_word_is_used(string? name, string? email, string expected) =>
        Assert.Equal(expected, TravelProfile.New(Fares, name, email, Created).DisplayName);

    [Fact]
    public void A_long_sign_in_name_is_cut_to_60_characters() =>
        Assert.Equal(60, TravelProfile.New(Fares, new string('a', 80), null, Created).DisplayName.Length);

    [Fact]
    public void An_update_replaces_the_editable_fields_and_keeps_the_rest()
    {
        var profile = TravelProfile.New(Fares, "Fares", "fares@example.com", Created).Update(Changes(name: " Fares H "), Known, Later);

        Assert.Equal(("Fares H", "LB", "EUR", "276781"), (profile.DisplayName, profile.Passport, profile.HomeCurrency, profile.HomeCityId));
        Assert.Equal(new Weights(90, 90, 50, 50, 20, 50), profile.Preferences);
        Assert.Equal(("fares@example.com", Created, Later), (profile.Email, profile.CreatedAt, profile.UpdatedAt));
        Assert.Equal(Fares, profile.Traveller);
    }

    [Fact]
    public void An_empty_home_city_means_none() =>
        Assert.Null(TravelProfile.New(Fares, "Fares", null, Created).Update(Changes(city: " "), Known, Later).HomeCityId);

    [Theory]
    [InlineData("", "lb", "usd", "276781", 50, "displayName")]
    [InlineData(null, "lb", "usd", "276781", 50, "displayName")]
    [InlineData("Fares", "XX", "usd", "276781", 50, "passport")]
    [InlineData("Fares", null, "usd", "276781", 50, "passport")]
    [InlineData("Fares", "lb", "ABC", "276781", 50, "homeCurrency")]
    [InlineData("Fares", "lb", "usd", "999", 50, "homeCityId")]
    [InlineData("Fares", "lb", "usd", null, 101, "budget")]
    [InlineData("Fares", "lb", "usd", null, -1, "budget")]
    [InlineData("Fares", "lb", "usd", null, null, "budget")]
    public void A_bad_value_is_refused_naming_its_field(string? name, string? passport, string? currency, string? city, int? budget, string field)
    {
        var profile = TravelProfile.New(Fares, "Fares", null, Created);

        var error = Assert.Throws<DomainException>(() => profile.Update(Changes(name, passport, currency, city, budget), Known, Later));

        Assert.Equal(field, error.Field);
    }

    [Fact]
    public void A_name_over_60_characters_is_refused() =>
        Assert.Equal("displayName", Assert.Throws<DomainException>(() =>
            TravelProfile.New(Fares, "Fares", null, Created).Update(Changes(name: new string('a', 61)), Known, Later)).Field);

    [Fact]
    public void A_missing_preference_is_refused_naming_it() =>
        Assert.Equal("culture", Assert.Throws<DomainException>(() =>
            TravelProfile.New(Fares, "Fares", null, Created).Update(Changes(culture: null), Known, Later)).Field);
}

public class TravellerIdTests
{
    [Fact]
    public void Never_shows_the_id_when_printed()
    {
        var id = new TravellerId("uid-secret");

        Assert.Equal("traveller", id.ToString());
        Assert.Equal("traveller", $"{id}");
        Assert.Equal("uid-secret", id.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Is_never_empty(string value) => Assert.Throws<DomainException>(() => new TravellerId(value));

    [Fact]
    public void Is_at_most_128_characters()
    {
        Assert.Equal(128, new TravellerId(new string('a', 128)).Value.Length);
        Assert.Throws<DomainException>(() => new TravellerId(new string('a', 129)));
    }

    [Fact]
    public void Compares_by_value() => Assert.Equal(new TravellerId("a"), new TravellerId("a"));
}
