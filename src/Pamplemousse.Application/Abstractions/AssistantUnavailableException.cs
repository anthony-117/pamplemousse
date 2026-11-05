namespace Pamplemousse.Application.Abstractions;

/// <summary>The chat model is busy, over its free limit, too slow or refused the call. The reply becomes a warning (spec 003, FR-008).</summary>
public sealed class AssistantUnavailableException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
