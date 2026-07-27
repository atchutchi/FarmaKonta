using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.Application.Identity.Authentication;

public sealed record AuthenticationIdentity(
    LocalUser User,
    CredentialHash Credential,
    EntityId PharmacyId,
    EntityId DeviceId);
