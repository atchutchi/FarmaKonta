using Nofarma.Domain.Identity;

namespace Nofarma.Application.Identity.Users;

public sealed record ManagedUserIdentity(
    LocalUser User,
    UserAdministrationContext Context);
