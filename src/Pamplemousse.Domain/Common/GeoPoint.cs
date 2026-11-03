using System.Runtime.InteropServices;

namespace Pamplemousse.Domain.Common;

/// <summary>A WGS84 coordinate.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct GeoPoint(double Lat, double Lng)
{
    private const double EarthRadiusKm = 6371;

    /// <summary>Straight-line (great-circle) distance in kilometres.</summary>
    public double DistanceKmTo(GeoPoint other)
    {
        var dLat = Radians(other.Lat - Lat);
        var dLng = Radians(other.Lng - Lng);
        var h = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2)) + (Math.Cos(Radians(Lat)) * Math.Cos(Radians(other.Lat)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2));
        return 2 * EarthRadiusKm * Math.Asin(Math.Sqrt(h));
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}
