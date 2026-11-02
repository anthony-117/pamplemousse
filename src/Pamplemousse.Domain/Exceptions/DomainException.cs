namespace Pamplemousse.Domain.Exceptions;

/// <summary>A business rule was broken. <see cref="Field"/> names the input at fault, when there is one.</summary>
public class DomainException(string message, string? field = null) : Exception(message)
{
    public string? Field { get; } = field;
}

/// <summary>The requested thing does not exist.</summary>
public sealed class NotFoundException(string message) : DomainException(message);
