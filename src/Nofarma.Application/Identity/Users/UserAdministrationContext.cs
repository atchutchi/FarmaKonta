using Nofarma.Domain.Common;

namespace Nofarma.Application.Identity.Users;

public sealed record UserAdministrationContext(
    EntityId PharmacyId,
    EntityId DeviceId);
