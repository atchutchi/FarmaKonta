using Nofarma.Domain.Common;

namespace Nofarma.Domain.Supply;

public sealed class Supplier
{
    private Supplier(EntityId id, EntityId pharmacyId)
    {
        Id = id;
        PharmacyId = pharmacyId;
        Name = string.Empty;
        IsActive = true;
    }

    public EntityId Id { get; }

    public EntityId PharmacyId { get; }

    public string Name { get; private set; }

    public string? TaxIdentifier { get; private set; }

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    public string? Address { get; private set; }

    public string? Notes { get; private set; }

    public bool IsActive { get; private set; }

    public static Supplier Create(
        EntityId id,
        EntityId pharmacyId,
        string name,
        string? taxIdentifier,
        string? phone,
        string? email,
        string? address,
        string? notes)
    {
        EnsureIdentifier(id, "O fornecedor deve ter um identificador.");
        EnsureIdentifier(pharmacyId, "A farmácia deve ter um identificador.");
        var supplier = new Supplier(id, pharmacyId);
        supplier.Update(name, taxIdentifier, phone, email, address, notes);
        return supplier;
    }

    public void Update(
        string name,
        string? taxIdentifier,
        string? phone,
        string? email,
        string? address,
        string? notes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new SupplierValidationException("O nome do fornecedor é obrigatório.");
        }

        Name = name.Trim();
        TaxIdentifier = NormalizeOptional(taxIdentifier);
        Phone = NormalizeOptional(phone);
        Email = NormalizeOptional(email);
        Address = NormalizeOptional(address);
        Notes = NormalizeOptional(notes);
    }

    public void Deactivate() => IsActive = false;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureIdentifier(EntityId id, string message)
    {
        if (id.Value == Guid.Empty)
        {
            throw new SupplierValidationException(message);
        }
    }
}
