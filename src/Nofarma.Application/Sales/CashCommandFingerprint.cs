using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nofarma.Domain.Common;
using Nofarma.Domain.Sales;

namespace Nofarma.Application.Sales;

internal static class CashCommandFingerprint
{
    public static string ForOpen(
        EntityId pharmacyId,
        EntityId deviceId,
        long openingCashXof) => Hash(
        "open",
        pharmacyId.Value.ToString("N"),
        deviceId.Value.ToString("N"),
        openingCashXof.ToString(CultureInfo.InvariantCulture));

    public static string ForManual(
        EntityId pharmacyId,
        EntityId deviceId,
        CashMovementType type,
        long amountXof,
        string reason) => Hash(
            "manual",
            pharmacyId.Value.ToString("N"),
            deviceId.Value.ToString("N"),
            ((int)type).ToString(CultureInfo.InvariantCulture),
            amountXof.ToString(CultureInfo.InvariantCulture),
            reason);

    public static string ForClose(
        EntityId pharmacyId,
        EntityId deviceId,
        long countedCashXof) => Hash(
        "close",
        pharmacyId.Value.ToString("N"),
        deviceId.Value.ToString("N"),
        countedCashXof.ToString(CultureInfo.InvariantCulture));

    private static string Hash(string operation, params string[] values)
    {
        var canonical = new StringBuilder();
        Append(canonical, operation);
        foreach (string value in values)
        {
            Append(canonical, value);
        }

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexString(digest);
    }

    private static void Append(StringBuilder target, string value) => target
        .Append(value.Length.ToString(CultureInfo.InvariantCulture))
        .Append(':')
        .Append(value)
        .Append('|');
}
