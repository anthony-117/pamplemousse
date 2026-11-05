using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;

namespace Pamplemousse.Api.Tests;

/// <summary>The same places around every position; fails while <see cref="Error"/> is set.</summary>
public sealed class FakeNearby : INearbyPlaces
{
    public Exception? Error { get; set; }

    public Task<NearbyCounts> CountAsync(GeoPoint position, CancellationToken ct) =>
        Error is null ? Task.FromResult(new NearbyCounts(120, 15, 4)) : Task.FromException<NearbyCounts>(Error);
}
