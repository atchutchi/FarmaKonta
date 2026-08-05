using Nofarma.Domain.Common;

namespace Nofarma.Domain.Sales;

public sealed class SuspendedSale
{
    private SuspendedSale(
        EntityId id,
        EntityId pharmacyId,
        EntityId deviceId,
        EntityId userId,
        string? name,
        IReadOnlyList<SaleLine> lines,
        UtcInstant suspendedAt)
    {
        Id = id;
        PharmacyId = pharmacyId;
        DeviceId = deviceId;
        UserId = userId;
        Name = name;
        Lines = lines;
        SuspendedAt = suspendedAt;
    }

    public EntityId Id { get; }
    public EntityId PharmacyId { get; }
    public EntityId DeviceId { get; }
    public EntityId UserId { get; }
    public string? Name { get; }
    public IReadOnlyList<SaleLine> Lines { get; }
    public UtcInstant SuspendedAt { get; }

    public static SuspendedSale Create(
        EntityId id,
        EntityId pharmacyId,
        EntityId deviceId,
        EntityId userId,
        string? name,
        IReadOnlyCollection<SaleLine> lines,
        UtcInstant suspendedAt)
    {
        EnsureIdentifier(id, "A venda suspensa deve ter um identificador.");
        EnsureIdentifier(pharmacyId, "A farmácia da venda suspensa é obrigatória.");
        EnsureIdentifier(deviceId, "O dispositivo da venda suspensa é obrigatório.");
        EnsureIdentifier(userId, "O utilizador da venda suspensa é obrigatório.");
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new SalesValidationException("A venda suspensa deve ter pelo menos uma linha.");
        }
        if (suspendedAt == default)
        {
            throw new SalesValidationException("A data da suspensão é obrigatória.");
        }
        string? normalizedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (normalizedName?.Length > 120)
        {
            throw new SalesValidationException(
                "O nome da venda suspensa não pode exceder 120 caracteres.");
        }

        return new SuspendedSale(
            id,
            pharmacyId,
            deviceId,
            userId,
            normalizedName,
            Array.AsReadOnly(lines.ToArray()),
            suspendedAt);
    }

    private static void EnsureIdentifier(EntityId id, string message)
    {
        if (id.Value == Guid.Empty)
        {
            throw new SalesValidationException(message);
        }
    }
}
