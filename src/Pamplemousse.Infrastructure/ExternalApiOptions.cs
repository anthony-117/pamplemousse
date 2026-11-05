namespace Pamplemousse.Infrastructure;

/// <summary>API keys for registered providers. Filled from Env by the API at startup; empty = not configured.</summary>
public sealed class ExternalApiOptions
{
    public string OpenTripMapApiKey { get; set; } = "";

    public string TicketmasterApiKey { get; set; } = "";

    public string TravelpayoutsApiToken { get; set; } = "";

    /// <summary>Partner ID added to booking links; flights work without it but earn no commission.</summary>
    public string TravelpayoutsMarker { get; set; } = "";

    /// <summary>Booking COM API on RapidAPI: hotels (ADR 0016).</summary>
    public string RapidApiKey { get; set; } = "";

    /// <summary>Requests a month on the RapidAPI plan; new calls stop at 95% (ADR 0017). The free Basic plan allows 530.</summary>
    public int RapidApiMonthlyLimit { get; set; } = 530;

    /// <summary>Firebase project for Firestore; empty keeps hotel data in memory only.</summary>
    public string FirebaseProjectId { get; set; } = "";

    /// <summary>Path to the service account JSON file; empty uses Google's default credentials (or the emulator).</summary>
    public string GoogleCredentialsPath { get; set; } = "";

    /// <summary>Google AI Studio key for the assistant (ADR 0005, ADR 0018); empty makes every answer a warning.</summary>
    public string GeminiApiKey { get; set; } = "";

    /// <summary>Gemini model the assistant uses.</summary>
    public string GeminiModel { get; set; } = "gemini-2.5-flash";

    /// <summary>Longest wait for one Gemini call; past it the answer is the "busy" warning (spec 003).</summary>
    public TimeSpan GeminiTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Most rounds of tool calls in one answer (spec 003, edge cases).</summary>
    public int AssistantMaxToolRounds { get; set; } = 6;
}
