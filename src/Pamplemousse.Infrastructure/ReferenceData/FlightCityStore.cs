using System.Globalization;
using System.IO.Compression;
using System.Text;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Flights;

namespace Pamplemousse.Infrastructure.ReferenceData;

/// <summary>
/// Cities with a real airport and airline names, embedded in this assembly and loaded once.
/// Source: Travelpayouts' public lists, rebuilt by scripts/update-flight-data.py.
/// </summary>
public sealed class FlightCityStore : IFlightCities
{
    /// <summary>A realistic trip to an airport; further than this, a city counts as having none.</summary>
    private const double MaxDistanceKm = 150;

    private readonly Lazy<List<FlightCity>> _cities = new(LoadCities);
    private readonly Lazy<Dictionary<string, string>> _airlines = new(LoadAirlines);

    public CityMatch? FindNearest(GeoPoint location)
    {
        FlightCity? nearest = null;
        var best = double.MaxValue;
        foreach (var city in _cities.Value)
        {
            var distance = location.DistanceKmTo(city.Location);
            if (distance < best)
            {
                (nearest, best) = (city, distance);
            }
        }

        return nearest is not null && best <= MaxDistanceKm ? new CityMatch(nearest, Math.Round(best)) : null;
    }

    /// <summary>The airline's name; its code when the name is unknown.</summary>
    public string AirlineName(string code) => _airlines.Value.GetValueOrDefault(code, code);

    private static List<FlightCity> LoadCities()
    {
        var cities = new List<FlightCity>(4_000);
        foreach (var line in ReadLines("flight-cities.tsv.gz"))
        {
            // code, name, country, lat, lng
            var c = line.Split('\t');
            cities.Add(new FlightCity(c[0], c[1], c[2], new GeoPoint(double.Parse(c[3], CultureInfo.InvariantCulture), double.Parse(c[4], CultureInfo.InvariantCulture))));
        }

        return cities;
    }

    private static Dictionary<string, string> LoadAirlines()
    {
        var airlines = new Dictionary<string, string>(1_500, StringComparer.Ordinal);
        foreach (var line in ReadLines("airlines.tsv.gz"))
        {
            var c = line.Split('\t');
            airlines[c[0]] = c[1];
        }

        return airlines;
    }

    private static List<string> ReadLines(string file)
    {
        var name = $"ReferenceData.{file}";
        using var stream = typeof(FlightCityStore).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing embedded resource {name}.");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }
}
