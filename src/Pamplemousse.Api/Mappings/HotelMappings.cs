using Pamplemousse.Api.ViewModels;
using Pamplemousse.Application.Hotels.Queries.GetHotel;
using Pamplemousse.Application.Hotels.Queries.SearchHotels;
using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Api.Mappings;

internal static class HotelMappings
{
    public static HotelSearchViewModel ToViewModel(this SearchHotelsResponse r) => new(
        new HotelDestinationViewModel(r.Destination.Id, r.Destination.Name, r.Destination.SourceName),
        new StayDatesViewModel(r.Dates.From, r.Dates.To, r.Nights),
        r.Adults,
        r.Currency,
        r.Sort,
        r.Page,
        r.TotalItems,
        r.PricesFetchedAt,
        r.IsSavedPrice,
        [.. r.Items.Select(o => o.ToViewModel(r.Source))],
        r.FiltersCheckedOn,
        r.Message,
        r.SearchUrl,
        r.Warnings);

    public static HotelDetailsViewModel ToViewModel(this GetHotelResponse r)
    {
        var h = r.Hotel;
        return new HotelDetailsViewModel(
            h.Id,
            h.Name,
            h.PhotoUrls,
            h.Stars,
            h.StarsEstimated,
            h.ReviewScore,
            h.ReviewCount,
            h.ReviewWord,
            new GeoPointViewModel(h.Location.Lat, h.Location.Lng),
            h.Address,
            h.District,
            h.DistanceToCentreKm,
            h.StayType,
            h.FreeCancellation,
            h.BreakfastIncluded,
            h.CheckInFrom,
            h.CheckInUntil,
            h.CheckOutFrom,
            h.CheckOutUntil,
            h.Facilities,
            h.Languages,
            r.Price is { } p ? new StayPriceViewModel(p.Total, p.PerNight, p.Currency) : null,
            r.Price?.FetchedAt,
            r.BookingUrl,
            h.DetailsUpdatedAt,
            r.Source,
            r.Warnings);
    }

    private static HotelViewModel ToViewModel(this HotelOffer o, string source) => new(
        o.Hotel.Id,
        o.Hotel.Name,
        o.Hotel.PhotoUrls.Count > 0 ? o.Hotel.PhotoUrls[0] : null,
        o.Hotel.Stars,
        o.Hotel.ReviewScore,
        o.Hotel.ReviewCount,
        new GeoPointViewModel(o.Hotel.Location.Lat, o.Hotel.Location.Lng),
        o.DistanceKm,
        o.NearCity,
        o.Hotel.StayType,
        o.Hotel.FreeCancellation,
        new StayPriceViewModel(o.Price.Total, o.Price.PerNight, o.Price.Currency),
        source);
}
