using Nofarma.Domain.Common;
using Nofarma.Domain.Sales;

namespace Nofarma.Application.Sales;

public sealed record CashShiftActorContext(
    EntityId PharmacyId,
    EntityId DeviceId,
    EntityId ActorUserId);

public sealed record CashShiftSummary(
    EntityId Id,
    EntityId PharmacyId,
    EntityId DeviceId,
    EntityId UserId,
    CashShiftStatus Status,
    long OpeningCashXof,
    long TotalEntriesXof,
    long TotalExitsXof,
    long ExpectedCashXof,
    long? CountedCashXof,
    long? DifferenceXof,
    UtcInstant OpenedAtUtc,
    UtcInstant? ClosedAtUtc,
    int MovementCount);

public sealed record StoredCashShift(CashShift Shift, long Version);
