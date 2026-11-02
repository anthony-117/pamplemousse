namespace Pamplemousse.Infrastructure;

/// <summary>API keys for registered providers. Filled from Env by the API at startup; empty = not configured.</summary>
public sealed class ExternalApiOptions
{
    public string OpenTripMapApiKey { get; set; } = "";

    public string TicketmasterApiKey { get; set; } = "";

    public string TravelpayoutsApiToken { get; set; } = "";

    /// <summary>Partner ID added to booking links; flights work without it but earn no commission.</summary>
    public string TravelpayoutsMarker { get; set; } = "";
}
