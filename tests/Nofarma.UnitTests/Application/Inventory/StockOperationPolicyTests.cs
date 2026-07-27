using Nofarma.Application.Abstractions;
using Nofarma.Application.Configuration;
using Nofarma.Application.Inventory;
using Nofarma.Domain.Identity;
using Nofarma.Infrastructure.Licensing;

namespace Nofarma.UnitTests.Application.Inventory;

public sealed class StockOperationPolicyTests
{
    [Theory]
    [InlineData(InstallationStatus.Preparing)]
    [InlineData(InstallationStatus.ReadyForActivation)]
    public async Task UnlicensedInstallationBlocksStockConfirmation(
        InstallationStatus status)
    {
        var policy = new InstallationStockOperationPolicy(new ApplicationInfoStore(status));

        StockOperationPolicyResult result = await policy.CanConfirmAsync(
            CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Equal("LICENSE_REQUIRED", result.Code);
    }

    [Fact]
    public async Task ActiveInstallationAllowsStockConfirmation()
    {
        var policy = new InstallationStockOperationPolicy(
            new ApplicationInfoStore(InstallationStatus.Active));

        StockOperationPolicyResult result = await policy.CanConfirmAsync(
            CancellationToken.None);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Code);
    }

    [Fact]
    public async Task MissingInstallationBlocksStockConfirmation()
    {
        var policy = new InstallationStockOperationPolicy(new ApplicationInfoStore(info: null));

        StockOperationPolicyResult result = await policy.CanConfirmAsync(
            CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Equal("INSTALLATION_REQUIRED", result.Code);
    }

    private sealed class ApplicationInfoStore : ILocalApplicationInfoStore
    {
        private readonly LocalApplicationInfo? _info;

        public ApplicationInfoStore(InstallationStatus status)
            : this(new LocalApplicationInfo(
                "Farmácia de teste",
                "500000000",
                "Bissau",
                string.Empty,
                status))
        {
        }

        public ApplicationInfoStore(LocalApplicationInfo? info)
        {
            _info = info;
        }

        public Task<LocalApplicationInfo?> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_info);
    }
}
