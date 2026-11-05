using System.Globalization;
using Pamplemousse.Domain.Cars;

namespace Pamplemousse.Domain.Ranking;

/// <summary>Turns rental cars into candidates: budget, and comfort from size, company rating, gearbox and cancellation (research R-3).</summary>
public static class CarRanking
{
    public static IReadOnlyList<Criterion> Used { get; } = [Criterion.Budget, Criterion.Comfort];

    private static readonly Dictionary<string, double> SizeMeasure = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Mini"] = 0.2,
        ["Economy"] = 0.35,
        ["Compact"] = 0.5,
        ["Intermediate"] = 0.65,
        ["Standard"] = 0.75,
        ["Full-size"] = 0.85,
        ["Fullsize"] = 0.85,
        ["Premium"] = 1,
        ["Luxury"] = 1,
        ["SUV"] = 1,
        ["People carrier"] = 1,
    };

    public static IReadOnlyList<Ranked<CarOffer>> Rank(IReadOnlyList<CarOffer> offers, Preferences preferences, string currency)
    {
        ArgumentNullException.ThrowIfNull(offers);
        var prices = offers.Select(o => o.Price.Total).ToList();
        return ScoringEngine.Rank(offers, o => Describe(o, prices), preferences, currency);
    }

    private static Candidate Describe(CarOffer car, List<decimal> prices)
    {
        var price = car.Price.Total;
        var automatic = car.Gearbox == Gearbox.Automatic;
        var criteria = new Dictionary<Criterion, CriterionInput>
        {
            [Criterion.Budget] = new(
                [new Part((double)price, Scale.LowerIsBetter)],
                $"{PriceText.Format(price, car.Price.Currency)}, {PriceText.CheapestRank(PriceText.PositionOf(price, prices), prices.Count)}",
                "Cheap",
                "Good price",
                "Cheapest option"),
            [Criterion.Comfort] = new(
                [
                    new Part(SizeMeasure.TryGetValue(car.Size, out var size) ? size : null, Scale.Fixed),
                    new Part(car.Company.Rating / 10, Scale.Fixed),
                    new Part(car.Gearbox is null ? null : automatic ? 1 : 0, Scale.Fixed),
                    new Part(car.FreeCancellation is null ? null : car.FreeCancellation.Value ? 1 : 0, Scale.Fixed),
                ],
                ComfortDetail(car),
                automatic ? "Roomy, automatic" : "Roomy",
                "Well rated"),
        };
        return new Candidate(car.Id, car.Rank, price, criteria);
    }

    private static string ComfortDetail(CarOffer car)
    {
        var parts = new List<string> { car.Size };
        if (car.Gearbox is { } gearbox)
        {
            parts.Add(gearbox == Gearbox.Automatic ? "automatic" : "manual");
        }

        if (car.Company.Rating is { } rating)
        {
            parts.Add($"{car.Company.Name} {rating.ToString("0.0", CultureInfo.InvariantCulture)} / 10");
        }

        if (car.FreeCancellation == true)
        {
            parts.Add("free cancellation");
        }

        return string.Join(" · ", parts);
    }
}
