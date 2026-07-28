using Nofarma.Application.Licensing;
using Nofarma.Domain.Common;

namespace Nofarma.Application.Abstractions;

public interface ILicenseClockCheckpoint
{
    LicenseClockCheck CheckAndAdvance(UtcInstant now);
}
