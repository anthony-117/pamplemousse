using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Application.Travellers;

public sealed record GetProfileQuery(SignedInTraveller Traveller);

/// <summary>The signed-in traveller's profile (spec 009, Story 3).</summary>
public sealed class GetProfileHandler(Profiles profiles) : IQueryHandler<GetProfileQuery, ProfileResponse>
{
    public async Task<ProfileResponse> HandleAsync(GetProfileQuery query, CancellationToken ct) =>
        profiles.Show(await profiles.GetOrCreateAsync(query.Traveller, ct));
}

public sealed record UpdateProfileCommand(SignedInTraveller Traveller, ProfileChanges Changes);

/// <summary>Replaces the editable fields of the signed-in traveller's profile (FR-011); only ever their own (FR-013).</summary>
public sealed class UpdateProfileHandler(Profiles profiles, ITravelProfileStore store, IReferenceData data, TimeProvider clock)
    : ICommandHandler<UpdateProfileCommand, ProfileResponse>
{
    public async Task<ProfileResponse> HandleAsync(UpdateProfileCommand command, CancellationToken ct)
    {
        var current = await profiles.GetOrCreateAsync(command.Traveller, ct);
        var lookups = new ProfileLookups(
            code => data.FindCountry(code) is not null,
            code => data.Countries.Any(c => c.Currencies.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase))),
            id => data.FindDestination(id) is not null);

        var updated = current.Update(command.Changes, lookups, clock.GetUtcNow());
        await store.SaveAsync(updated, ct);
        return profiles.Show(updated);
    }
}

public sealed record DeleteAccountCommand(SignedInTraveller Traveller);

/// <summary>
/// Deletes everything stored for the traveller, then the sign-in itself (FR-014, research R-4). A failure stops at that
/// step, and a retry finishes the job since each step treats "already gone" as done.
/// </summary>
public sealed class DeleteAccountHandler(ITravelProfileStore store, IPlanStore plans, ISavedStore saved, IChatStore chats, IAccountDeleter accounts, IDeletedTravellers deleted)
    : ICommandHandler<DeleteAccountCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteAccountCommand command, CancellationToken ct)
    {
        var id = command.Traveller.Id;

        // Chats, saved items and plans first (specs 010, 008): a retry after a failure still finds the profile and finishes the job.
        await chats.DeleteAllAsync(id, ct);
        await saved.DeleteAllAsync(id, ct);
        await plans.DeleteAllAsync(id, ct);
        await store.DeleteAsync(id, ct);
        await accounts.DeleteAsync(id, ct);

        // The token stays valid for up to an hour; refuse it from now on (R-5).
        await deleted.AddAsync(id, ct);
        return true;
    }
}
