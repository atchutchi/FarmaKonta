using Nofarma.Application.Licensing;
using Nofarma.Domain.Common;

namespace Nofarma.Application.Abstractions;

public interface IDeviceLicenseIdentityStore
{
    DeviceLicenseIdentity GetOrCreate(EntityId pharmacyId, EntityId deviceId);
}
