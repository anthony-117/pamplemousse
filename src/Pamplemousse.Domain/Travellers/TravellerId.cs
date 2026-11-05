using Pamplemousse.Domain.Exceptions;

namespace Pamplemousse.Domain.Travellers;

/// <summary>
/// A traveller's Firebase user id (spec 009, FR-008). <see cref="ToString"/> never shows it, so it can't reach a log by
/// mistake (FR-016); use <see cref="Value"/> where the id itself is needed.
/// </summary>
public readonly record struct TravellerId
{
    public const int MaxLength = 128;

    public TravellerId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            throw new DomainException("A traveller id has 1 to 128 characters.", "traveller");
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => "traveller";
}
