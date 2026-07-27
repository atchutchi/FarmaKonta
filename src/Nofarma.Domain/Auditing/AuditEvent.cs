using Nofarma.Domain.Common;

namespace Nofarma.Domain.Auditing;

public sealed record AuditEvent(
    EntityId Id,
    EntityId PharmacyId,
    EntityId DeviceId,
    EntityId? UserId,
    string Action,
    string ObjectType,
    string ObjectId,
    UtcInstant OccurredAtUtc,
    AuditOutcome Outcome,
    string? DiagnosticCode,
    string DetailsJson);
