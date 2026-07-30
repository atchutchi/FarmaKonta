using Nofarma.Application.Licensing;
using Nofarma.Desktop.ViewModels;
using Nofarma.Infrastructure.Licensing;

namespace Nofarma.Desktop.Services;

public sealed record LicenseChannelContext(LicenseBuildChannel Channel);

public sealed class LicensePageOperations(
    LicenseService licenses,
    LicenseChannelContext channelContext) : ILicensePageOperations
{
    public event EventHandler? StatusChanged;

    public bool IsQa => channelContext.Channel == LicenseBuildChannel.Qa;

    public async Task<LicensePageSnapshot> LoadAsync(
        CancellationToken cancellationToken)
    {
        LicenseStatus status = await licenses.GetStatusAsync(cancellationToken);
        LicenseActivationRequest request = await licenses.CreateActivationRequestAsync(
            cancellationToken);
        return new LicensePageSnapshot(status, request.Device.PublicKeyThumbprint);
    }

    public async Task<ReadOnlyMemory<byte>> CreateRequestAsync(
        CancellationToken cancellationToken)
    {
        LicenseActivationRequest request = await licenses.CreateActivationRequestAsync(
            cancellationToken);
        return CanonicalLicenseActivationRequestJson.Serialize(
            request,
            channelContext.Channel);
    }

    public async Task ImportAsync(
        ReadOnlyMemory<byte> document,
        CancellationToken cancellationToken)
    {
        byte[] stableDocument = document.ToArray();
        _ = await licenses.ImportAsync(
            new LicenseImportRequest(stableDocument),
            cancellationToken);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
