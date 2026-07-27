using Nofarma.Domain.Common;

namespace Nofarma.Application.Identity.Setup;

public sealed record SetupResult(
    EntityId InstallationId,
    string RecoveryCode);
