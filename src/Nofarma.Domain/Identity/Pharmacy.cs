using Nofarma.Domain.Common;

namespace Nofarma.Domain.Identity;

public sealed class Pharmacy
{
    public Pharmacy(
        EntityId id,
        string name,
        string taxIdentifier,
        string address,
        string contact,
        string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(taxIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        Id = id;
        Name = name.Trim();
        TaxIdentifier = taxIdentifier.Trim();
        Address = address.Trim();
        Contact = contact.Trim();
        TimeZoneId = timeZoneId.Trim();
    }

    public EntityId Id { get; }

    public string Name { get; }

    public string TaxIdentifier { get; }

    public string Address { get; }

    public string Contact { get; }

    public string TimeZoneId { get; }
}
