using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Destinations;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Travellers;

/// <summary>
/// Who is asking, as the sign-in says (spec 009, FR-008). The API builds it from the token; another client (the
/// Telegram bot) can build it its own way.
/// </summary>
public sealed record SignedInTraveller(TravellerId Id, string? Email, string? Name);

/// <summary>A profile with its home city looked up, for showing.</summary>
public sealed record ProfileResponse(TravelProfile Profile, Destination? HomeCity);

/// <summary>Reads a traveller's profile, making it on the first sign-in (FR-010).</summary>
public sealed class Profiles(ITravelProfileStore store, IReferenceData data, TimeProvider clock)
{
    public async Task<TravelProfile> GetOrCreateAsync(SignedInTraveller traveller, CancellationToken ct)
    {
        var profile = await store.GetAsync(traveller.Id, ct);
        if (profile is not null)
        {
            return profile;
        }

        profile = TravelProfile.New(traveller.Id, traveller.Name, traveller.Email, clock.GetUtcNow());
        await store.SaveAsync(profile, ct);
        return profile;
    }

    public ProfileResponse Show(TravelProfile profile) =>
        new(profile, profile.HomeCityId is { } city ? data.FindDestination(city) : null);
}
