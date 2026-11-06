using Pamplemousse.Api.Contracts;
using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Travellers;
using Pamplemousse.Domain.Travellers;

namespace Pamplemousse.Api.Mappings;

public static class ProfileMappings
{
    public static ProfileViewModel ToViewModel(this ProfileResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var p = response.Profile;
        var w = p.Preferences;
        return new ProfileViewModel(
            p.DisplayName,
            p.Email,
            p.Passport,
            p.HomeCurrency,
            response.HomeCity is { } city ? new HomeCityViewModel(city.Id, city.Name, city.CountryCode) : null,
            new WeightsViewModel(w.Budget, w.Culture, w.Food, w.Nature, w.Nightlife, w.Comfort),
            p.CreatedAt,
            p.UpdatedAt);
    }

    public static ProfileChanges ToChanges(this UpdateProfileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var w = request.Preferences;
        return new ProfileChanges(
            request.DisplayName,
            request.Passport,
            request.HomeCurrency,
            request.HomeCityId,
            w?.Budget,
            w?.Culture,
            w?.Food,
            w?.Nature,
            w?.Nightlife,
            w?.Comfort);
    }
}
