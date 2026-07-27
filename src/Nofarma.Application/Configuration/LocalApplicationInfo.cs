using Nofarma.Domain.Identity;

namespace Nofarma.Application.Configuration;

public sealed record LocalApplicationInfo(
    string PharmacyName,
    string TaxIdentifier,
    string Address,
    string Contact,
    InstallationStatus InstallationStatus);
