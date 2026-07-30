using Nofarma.Application.Licensing;

namespace Nofarma.Application.Abstractions;

public interface ILicenseDocumentVerifier
{
    LicenseVerification Verify(
        ReadOnlyMemory<byte> document,
        DeviceLicenseIdentity device,
        LicenseContext context);
}
