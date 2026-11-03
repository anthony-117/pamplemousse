using System.Text.Json;
using Microsoft.Extensions.Logging;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Domain.Common;
using Pamplemousse.Domain.Exceptions;
using Pamplemousse.Domain.Hotels;

namespace Pamplemousse.Application.Hotels.Queries.GetHotel;

public sealed record GetHotelQuery(string HotelId, DateOnly? From, DateOnly? To, int? Adults, string? Currency);

/// <summary>
/// One hotel with its details and booking link. <see cref="Price"/> is the last search price, only when it was for the same
/// dates, travellers and currency.
/// </summary>
public sealed record GetHotelResponse(Hotel Hotel, HotelPrice? Price, string BookingUrl, string Source, IReadOnlyList<string> Warnings);

/// <summary>
/// Opening a hotel (spec 002, story 2). Details are fetched once and reused for a week, so opening the same hotel again
/// costs no request.
/// </summary>
public sealed partial class GetHotelHandler(
    IHotelProvider provider,
    ICityMappingStore cities,
    IHotelStore hotels,
    IPriceHistory history,
    TimeProvider clock,
    ILogger<GetHotelHandler> logger) : IQueryHandler<GetHotelQuery, GetHotelResponse>
{
    public async Task<GetHotelResponse> HandleAsync(GetHotelQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = clock.GetUtcNow();
        var dates = DateRange.ForTrip(query.From, query.To, DateOnly.FromDateTime(now.UtcDateTime));

        // The search key is not used here; the hotel id stands in for the destination.
        var search = HotelSearch.Create(query.HotelId, dates, query.Adults, query.Currency);
        var store = new HotelData(hotels, history, cities, logger);
        var warnings = new List<string>();

        var hotel = await store.HotelAsync(query.HotelId, ct);
        if (hotel is null || hotel.DetailsAreStale(now))
        {
            try
            {
                if (await provider.GetDetailsAsync(query.HotelId, search, ct) is { } details)
                {
                    hotel = hotel?.MergeWith(details) ?? details;
                    await store.SaveHotelsAsync([hotel], ct);
                }
            }
            catch (ProviderNotConfiguredException ex)
            {
                warnings.Add(ex.Message);
            }
            catch (QuotaExceededException)
            {
                warnings.Add("The monthly hotel request limit is nearly used, so hotel details may be missing.");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                LogDetailsFailed(logger, ex);
                warnings.Add("Hotel details could not be loaded right now.");
            }
        }

        if (hotel is null)
        {
            throw new NotFoundException($"Hotel '{query.HotelId}' was not found.");
        }

        var price = hotel.LastPrice is { } last
            && last.CheckIn == search.Dates.From && last.CheckOut == search.Dates.To && last.Adults == search.Adults
            && string.Equals(last.Currency, search.Currency, StringComparison.Ordinal)
                ? last
                : null;
        return new GetHotelResponse(hotel, price, provider.BookingUrl(hotel, search), provider.Source, [.. warnings, .. store.Warnings]);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hotel details failed")]
    private static partial void LogDetailsFailed(ILogger logger, Exception exception);
}
