namespace Nofarma.Domain.Licensing;

public enum LicenseState
{
    Missing = 0,
    Invalid = 1,
    NotYetValid = 2,
    Valid = 3,
    Grace = 4,
    ExpiredReadOnly = 5,
    ClockRollback = 6
}
