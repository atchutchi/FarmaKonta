using Nofarma.Domain.Identity;

namespace Nofarma.Application.Identity.Users;

public sealed record CreateUserRequest(
    string DisplayName,
    string Login,
    UserRole Role,
    string Credential);
