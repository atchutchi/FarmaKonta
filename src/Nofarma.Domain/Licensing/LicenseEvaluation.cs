namespace Nofarma.Domain.Licensing;

public sealed record LicenseEvaluation(
    LicenseState State,
    bool AllowsNewOperations,
    bool AllowsReadOnlyAccess);
