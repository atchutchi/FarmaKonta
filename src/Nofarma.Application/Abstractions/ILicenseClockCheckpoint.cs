using Nofarma.Application.Licensing;
using Nofarma.Domain.Common;

namespace Nofarma.Application.Abstractions;

public interface ILicenseClockCheckpoint
{
    void Initialize(LicenseClockBinding binding, UtcInstant now);

    LicenseClockCheck CheckAndAdvance(LicenseClockBinding binding, UtcInstant now);
}
