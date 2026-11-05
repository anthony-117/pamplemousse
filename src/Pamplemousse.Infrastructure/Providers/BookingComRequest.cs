using System.Text.Json;
using Pamplemousse.Application.Abstractions;
using Pamplemousse.Infrastructure.Caching;

namespace Pamplemousse.Infrastructure.Providers;

/// <summary>
/// One call to the Booking COM API on RapidAPI (ADR 0016), shared by hotels and cars. Every request counts toward the plan's
/// monthly limit, so each one is counted first and refused at 95% (ADR 0017). The key goes in a header, never in a URL.
/// </summary>
internal static class BookingComRequest
{
    public const string Host = "booking-com18.p.rapidapi.com";

    /// <summary>The parsed answer and its raw bytes. Throws when the key is missing, the limit is near, or the source refuses.</summary>
    public static async Task<(JsonDocument Doc, byte[] Raw)> SendAsync(
        HttpClient http, ExternalApiOptions options, IRequestQuota quota, string feature, string pathAndQuery, CancellationToken ct)
    {
        var key = options.RapidApiKey is { Length: > 0 } k ? k : throw new ProviderNotConfiguredException(feature, "RAPIDAPI_KEY");

        // Stop at 95% of the plan, keeping a margin for live checks and other features sharing the key.
        var limit = (long)Math.Floor(options.RapidApiMonthlyLimit * 0.95);
        if (!await quota.TryTakeAsync(BookingComHotelProvider.QuotaSource, limit, ct))
        {
            throw new QuotaExceededException("Booking COM API");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://{Host}{pathAndQuery}"));
        request.Headers.Add("x-rapidapi-key", key);
        request.Headers.Add("x-rapidapi-host", Host);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsByteArrayAsync(ct);
        var doc = JsonDocument.Parse(raw);

        // The source reports some failures inside a 200 answer.
        if (doc.RootElement.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.False)
        {
            var message = ((JsonElement?)doc.RootElement).Prop("message").Str();
            doc.Dispose();
            throw new HttpRequestException($"The {feature.ToLowerInvariant()} source refused the request: {message}");
        }

        return (doc, raw);
    }
}
