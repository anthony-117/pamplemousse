namespace Pamplemousse.Domain.Common;

/// <summary>A WGS84 coordinate.</summary>
public readonly record struct GeoPoint(double Lat, double Lng);
