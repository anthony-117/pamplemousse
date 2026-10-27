using System.Runtime.InteropServices;

namespace Pamplemousse.Domain.Common;

/// <summary>A WGS84 coordinate.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct GeoPoint(double Lat, double Lng);
