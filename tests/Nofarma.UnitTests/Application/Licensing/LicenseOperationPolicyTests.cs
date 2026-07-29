using Nofarma.Application.Abstractions;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Licensing;
using Nofarma.Infrastructure.Licensing;

namespace Nofarma.UnitTests.Application.Licensing;

public sealed class LicenseOperationPolicyTests
{
    [Theory]
    [InlineData(LicenseState.Valid, true, null)]
    [InlineData(LicenseState.Grace, true, null)]
    [InlineData(LicenseState.Missing, false, "LICENSE_MISSING")]
    [InlineData(LicenseState.Invalid, false, "LICENSE_INVALID")]
    [InlineData(LicenseState.NotYetValid, false, "LICENSE_NOT_YET_VALID")]
    [InlineData(LicenseState.ExpiredReadOnly, false, "LICENSE_EXPIRED_READ_ONLY")]
    [InlineData(LicenseState.ClockRollback, false, "LICENSE_CLOCK_ROLLBACK")]
    public async Task MapsEachVerifiedLicenseStateToItsLiteralOperationDecision(
        LicenseState state,
        bool expectedAllowed,
        string? expectedCode)
    {
        var policy = new LicenseOperationPolicy(new StatusProvider(
            new LicenseStatus(state, expectedAllowed, true, null)));

        LicensedOperationPolicyResult result = await policy.CanCreateAsync(CancellationToken.None);

        Assert.Equal(expectedAllowed, result.IsAllowed);
        Assert.Equal(expectedCode, result.Code);
    }

    [Fact]
    public async Task MissingLocalContextFailsClosedWithInstallationRequired()
    {
        var policy = new LicenseOperationPolicy(new StatusProvider(
            new LicenseContextUnavailableException()));

        LicensedOperationPolicyResult result = await policy.CanCreateAsync(CancellationToken.None);

        Assert.False(result.IsAllowed);
        Assert.Equal("INSTALLATION_REQUIRED", result.Code);
    }

    private sealed class StatusProvider : ILicenseStatusProvider
    {
        private readonly LicenseStatus? _status;
        private readonly Exception? _exception;

        public StatusProvider(LicenseStatus status) => _status = status;

        public StatusProvider(Exception exception) => _exception = exception;

        public Task<LicenseStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_status!);
        }
    }
}
