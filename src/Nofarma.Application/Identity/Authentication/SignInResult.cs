using Nofarma.Domain.Common;

namespace Nofarma.Application.Identity.Authentication;

public sealed record SignInResult(
    bool Succeeded,
    string Message,
    LocalSession? Session,
    UtcInstant? LockedUntilUtc);
