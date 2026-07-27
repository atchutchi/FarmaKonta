using Nofarma.Domain.Auditing;
using Nofarma.Infrastructure.Persistence.Records;

namespace Nofarma.Infrastructure.Persistence;

internal static class InventoryPersistenceMapper
{
    public static AuditEventRecord MapAudit(AuditEvent audit) => new()
    {
        Id = audit.Id.Value,
        PharmacyId = audit.PharmacyId.Value,
        DeviceId = audit.DeviceId.Value,
        UserId = audit.UserId?.Value,
        Action = audit.Action,
        ObjectType = audit.ObjectType,
        ObjectId = audit.ObjectId,
        OccurredAtUtc = audit.OccurredAtUtc.Value,
        Outcome = (int)audit.Outcome,
        DiagnosticCode = audit.DiagnosticCode,
        DetailsJson = audit.DetailsJson
    };
}
