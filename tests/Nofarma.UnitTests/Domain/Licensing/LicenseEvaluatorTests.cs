using Nofarma.Domain.Common;
using Nofarma.Domain.Licensing;
using Nofarma.UnitTests.TestSupport.Licensing;

namespace Nofarma.UnitTests.Domain.Licensing;

public sealed class LicenseEvaluatorTests
{
    [Theory]
    [InlineData("2026-08-01T00:00:00Z", LicenseState.Valid)]
    [InlineData("2026-08-31T23:59:59Z", LicenseState.Valid)]
    [InlineData("2026-09-01T00:00:00Z", LicenseState.Grace)]
    [InlineData("2026-09-07T23:59:59Z", LicenseState.Grace)]
    [InlineData("2026-09-08T00:00:00Z", LicenseState.ExpiredReadOnly)]
    public void EvaluatesInclusiveValidityAndSevenDayGrace(string now, LicenseState expected)
    {
        LicenseGrant grant = LicenseTestData.Monthly(
            "2026-08-01T00:00:00Z",
            "2026-08-31T23:59:59Z",
            "2026-09-07T23:59:59Z");

        LicenseEvaluation result = LicenseEvaluator.Evaluate(
            grant,
            LicenseTestData.Instant(now),
            clockRollback: false);

        Assert.Equal(expected, result.State);
    }

    [Theory]
    [InlineData("2026-01-01T00:00:00Z", LicenseState.Valid)]
    [InlineData("2026-12-31T23:59:59Z", LicenseState.Valid)]
    [InlineData("2027-01-01T00:00:00Z", LicenseState.Grace)]
    [InlineData("2027-01-07T23:59:59Z", LicenseState.Grace)]
    [InlineData("2027-01-08T00:00:00Z", LicenseState.ExpiredReadOnly)]
    public void EvaluatesAnnualInclusiveValidityAndSevenDayGrace(string now, LicenseState expected)
    {
        LicenseGrant grant = LicenseTestData.Annual(
            validFrom: "2026-01-01T00:00:00Z",
            validUntil: "2026-12-31T23:59:59Z",
            graceUntil: "2027-01-07T23:59:59Z");

        LicenseEvaluation result = LicenseEvaluator.Evaluate(
            grant,
            LicenseTestData.Instant(now),
            clockRollback: false);

        Assert.Equal(expected, result.State);
    }

    [Fact]
    public void ClockRollbackOverridesOtherwiseValidGrant()
    {
        LicenseEvaluation result = LicenseEvaluator.Evaluate(
            LicenseTestData.Monthly(
                "2026-08-01T00:00:00Z",
                "2026-08-31T23:59:59Z",
                "2026-09-07T23:59:59Z"),
            LicenseTestData.Instant("2026-08-10T12:00:00Z"),
            clockRollback: true);

        Assert.Equal(LicenseState.ClockRollback, result.State);
        Assert.False(result.AllowsNewOperations);
        Assert.True(result.AllowsReadOnlyAccess);
    }

    [Fact]
    public void BeforeValidityStartsAllowsReadOnlyAccessOnly()
    {
        LicenseEvaluation result = LicenseEvaluator.Evaluate(
            LicenseTestData.Monthly(
                "2026-08-01T00:00:00Z",
                "2026-08-31T23:59:59Z",
                "2026-09-07T23:59:59Z"),
            LicenseTestData.Instant("2026-07-31T23:59:59Z"),
            clockRollback: false);

        Assert.Equal(LicenseState.NotYetValid, result.State);
        Assert.False(result.AllowsNewOperations);
        Assert.True(result.AllowsReadOnlyAccess);
    }

    [Fact]
    public void GrantRejectsAnEmptyLicenseIdentifier()
    {
        Assert.Throws<ArgumentException>(() => LicenseTestData.Create(id: new EntityId(Guid.Empty)));
    }

    [Fact]
    public void GrantRejectsAnEmptyPharmacyIdentifier()
    {
        Assert.Throws<ArgumentException>(() => LicenseTestData.Create(pharmacyId: new EntityId(Guid.Empty)));
    }

    [Fact]
    public void GrantRejectsAnEmptyEstablishmentIdentifier()
    {
        Assert.Throws<ArgumentException>(() => LicenseTestData.Create(establishmentId: new EntityId(Guid.Empty)));
    }

    [Fact]
    public void GrantRejectsAnEmptyDeviceIdentifier()
    {
        Assert.Throws<ArgumentException>(() => LicenseTestData.Create(deviceId: new EntityId(Guid.Empty)));
    }

    [Fact]
    public void GrantRejectsAnEstablishmentAssignedToAnotherPharmacy()
    {
        Assert.Throws<ArgumentException>(() => LicenseTestData.Create(
            establishmentId: LicenseTestData.OtherEstablishmentId));
    }

    [Fact]
    public void GrantRejectsASequenceBelowOne()
    {
        Assert.ThrowsAny<ArgumentException>(() => LicenseTestData.Create(sequence: 0));
    }

    [Fact]
    public void GrantRejectsAnIssueTimeAfterValidityEnds()
    {
        Assert.Throws<ArgumentException>(() => LicenseTestData.Create(
            issuedAt: "2026-09-01T00:00:00Z"));
    }

    [Fact]
    public void GrantRejectsAValidityStartAfterValidityEnds()
    {
        Assert.Throws<ArgumentException>(() => LicenseTestData.Create(
            validFrom: "2026-09-01T00:00:00Z"));
    }

    [Fact]
    public void GrantRejectsGraceThatIsNotExactlySevenDaysAfterValidityEnds()
    {
        Assert.Throws<ArgumentException>(() => LicenseTestData.Create(
            graceUntil: "2026-09-08T23:59:59Z"));
    }
}
