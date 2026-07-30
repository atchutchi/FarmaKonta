using Nofarma.Domain.Common;

namespace Nofarma.Domain.Licensing;

public static class LicenseEvaluator
{
    public static LicenseEvaluation Evaluate(LicenseGrant grant, UtcInstant now, bool clockRollback)
    {
        ArgumentNullException.ThrowIfNull(grant);

        if (clockRollback)
        {
            return new LicenseEvaluation(LicenseState.ClockRollback, false, true);
        }

        if (now.Value < grant.ValidFromUtc.Value)
        {
            return new LicenseEvaluation(LicenseState.NotYetValid, false, true);
        }

        if (now.Value <= grant.ValidUntilUtc.Value)
        {
            return new LicenseEvaluation(LicenseState.Valid, true, true);
        }

        if (now.Value <= grant.GraceUntilUtc.Value)
        {
            return new LicenseEvaluation(LicenseState.Grace, true, true);
        }

        return new LicenseEvaluation(LicenseState.ExpiredReadOnly, false, true);
    }
}
