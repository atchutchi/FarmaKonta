using Nofarma.Application.Sales;
using Nofarma.Domain.Auditing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Sales;

namespace Nofarma.Application.Abstractions;

public interface ICashShiftStore
{
    Task<CashShiftActorContext?> GetActorContextAsync(
        EntityId userId,
        CancellationToken cancellationToken);

    Task<StoredCashShift?> GetCurrentAggregateAsync(
        EntityId pharmacyId,
        EntityId deviceId,
        CancellationToken cancellationToken);

    Task<CashCommandResult?> GetCommandResultAsync(
        EntityId pharmacyId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<CashShiftSummary> SaveOpenedAsync(
        CashShiftActorContext context,
        CashShift shift,
        CashCommandEnvelope command,
        AuditEvent audit,
        CancellationToken cancellationToken);

    Task<CashShiftSummary> SaveMovementAsync(
        CashShiftActorContext context,
        CashShift shift,
        CashMovement movement,
        long expectedVersion,
        CashCommandEnvelope command,
        AuditEvent audit,
        CancellationToken cancellationToken);

    Task<CashShiftSummary> SaveClosedAsync(
        CashShiftActorContext context,
        CashShift shift,
        long expectedVersion,
        CashCommandEnvelope command,
        AuditEvent audit,
        CancellationToken cancellationToken);
}
