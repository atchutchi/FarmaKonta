using Nofarma.Domain.Common;

namespace Nofarma.Application.Supply;

public sealed record CreateSupplierRequest(
    string Name,
    string? TaxIdentifier,
    string? Phone,
    string? Email,
    string? Address,
    string? Notes);

public sealed record SupplierActorContext(EntityId PharmacyId, EntityId DeviceId);

public sealed record UpdateSupplierRequest(
    string Name,
    string? TaxIdentifier,
    string? Phone,
    string? Email,
    string? Address,
    string? Notes);
