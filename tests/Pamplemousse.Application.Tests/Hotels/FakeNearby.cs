using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;

namespace Pamplemousse.Application.Tests.Hotels;

/// <summary>Places around a hotel: busy within 5 km north of Rome's centre, quiet and green beyond. Can fail on demand.</summary>
internal sealed class FakeNearby : INearbyPlaces
{
    public Exception? Error { get; set; }

    public int Calls { get; private set; }

    public Task<NearbyCounts> CountAsync(GeoPoint position, CancellationToken ct)
    {
        Calls++;
        return Error is not null
            ? Task.FromException<NearbyCounts>(Error)
            : Task.FromResult(position.Lat < 41.89 + (5 / 111.2) ? new NearbyCounts(300, 40, 5) : new NearbyCounts(10, 2, 20));
    }
}
