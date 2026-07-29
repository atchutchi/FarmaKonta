using Nofarma.Domain.Licensing;

namespace Nofarma.Application.Licensing;

public static class LicenseOperationPolicyRules
{
    public static LicensedOperationPolicyResult For(LicenseState state) => state switch
    {
        LicenseState.Valid or LicenseState.Grace => new LicensedOperationPolicyResult(true, null),
        LicenseState.Missing => new LicensedOperationPolicyResult(false, "LICENSE_MISSING"),
        LicenseState.Invalid => new LicensedOperationPolicyResult(false, "LICENSE_INVALID"),
        LicenseState.NotYetValid => new LicensedOperationPolicyResult(false, "LICENSE_NOT_YET_VALID"),
        LicenseState.ExpiredReadOnly => new LicensedOperationPolicyResult(false, "LICENSE_EXPIRED_READ_ONLY"),
        LicenseState.ClockRollback => new LicensedOperationPolicyResult(false, "LICENSE_CLOCK_ROLLBACK"),
        _ => new LicensedOperationPolicyResult(false, "LICENSE_OPERATION_BLOCKED")
    };
}
