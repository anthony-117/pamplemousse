using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Ranking;

/// <summary>What a preference is about. Culture, food, nature and nightlife are about where you stay, so only hotels use them.</summary>
public enum Criterion
{
    Budget,
    Culture,
    Food,
    Nature,
    Nightlife,
    Comfort,
}

/// <summary>How much each preference matters, from 0 ("doesn't matter") to 100. The app's Low, Medium and High send 20, 50 and 90.</summary>
public sealed record Weights(int Budget, int Culture, int Food, int Nature, int Nightlife, int Comfort)
{
    /// <summary>The weight of a preference that wasn't sent.</summary>
    public const int Default = 50;

    public static Weights Neutral { get; } = new(Default, Default, Default, Default, Default, Default);

    public int this[Criterion criterion] => criterion switch
    {
        Criterion.Budget => Budget,
        Criterion.Culture => Culture,
        Criterion.Food => Food,
        Criterion.Nature => Nature,
        Criterion.Nightlife => Nightlife,
        Criterion.Comfort => Comfort,
        _ => throw new ArgumentOutOfRangeException(nameof(criterion), criterion, "Unknown criterion."),
    };

    public static Weights Create(int? budget, int? culture, int? food, int? nature, int? nightlife, int? comfort) => new(
        Check(budget, "budget"),
        Check(culture, "culture"),
        Check(food, "food"),
        Check(nature, "nature"),
        Check(nightlife, "nightlife"),
        Check(comfort, "comfort"));

    private static int Check(int? weight, string field) => weight switch
    {
        null => Default,
        < 0 or > 100 => throw new DomainException("Use a whole number from 0 to 100.", field),
        _ => weight.Value,
    };
}

/// <summary>A traveller's preferences and total budget for a ranked list (spec 007, FR-001 and FR-002).</summary>
public sealed record Preferences(Weights Weights, decimal? TripBudget)
{
    /// <summary>Null when nothing was sent, so the list is not ranked unless sort=match asks for it.</summary>
    public static Preferences? Create(int? budget, int? culture, int? food, int? nature, int? nightlife, int? comfort, decimal? tripBudget)
    {
        if (tripBudget is <= 0)
        {
            throw new DomainException("The trip budget must be more than 0.", "tripBudget");
        }

        var weights = Weights.Create(budget, culture, food, nature, nightlife, comfort);
        var nothingSent = budget is null && culture is null && food is null && nature is null && nightlife is null && comfort is null && tripBudget is null;
        return nothingSent ? null : new Preferences(weights, tripBudget);
    }
}
