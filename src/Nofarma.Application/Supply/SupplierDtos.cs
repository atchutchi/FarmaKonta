using Nofarma.Domain.Common;

namespace Nofarma.Application.Supply;

public sealed record SupplierSummary(EntityId Id, string Name, string? Phone, bool IsActive);

public sealed record SupplierDetails(
    EntityId Id,
    string Name,
    string? TaxIdentifier,
    string? Phone,
    string? Email,
    string? Address,
    string? Notes,
    bool IsActive);
