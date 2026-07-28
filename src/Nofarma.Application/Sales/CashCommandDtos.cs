namespace Nofarma.Application.Sales;

public enum CashCommandType
{
    OpenShift = 1,
    ManualEntry = 2,
    ManualExit = 3,
    CloseShift = 4
}

public sealed record CashCommandEnvelope(
    string IdempotencyKey,
    CashCommandType Type,
    string RequestFingerprint);

public sealed record CashCommandResult(
    string RequestFingerprint,
    CashShiftSummary Result);
