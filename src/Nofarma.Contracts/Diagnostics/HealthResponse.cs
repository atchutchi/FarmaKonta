namespace Nofarma.Contracts.Diagnostics;

public sealed record HealthResponse(
    string Service,
    string Status,
    string Version,
    DateTimeOffset CheckedAtUtc);
