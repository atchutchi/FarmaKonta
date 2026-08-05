using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Nofarma.Application.Sales;

public static class SaleCommandFingerprint
{
    public static string ForComplete(
        SaleActorContext context,
        CompleteSaleRequest request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        var canonical = new StringBuilder();
        Append(canonical, "complete-sale");
        Append(canonical, context.PharmacyId.Value.ToString("N"));
        Append(canonical, context.DeviceId.Value.ToString("N"));
        Append(canonical, context.ActorUserId.Value.ToString("N"));
        Append(canonical, request.TotalDiscountXof.ToString(CultureInfo.InvariantCulture));
        foreach (CompleteSaleLineRequest line in request.Lines
                     .OrderBy(item => item.ProductId.Value)
                     .ThenBy(item => item.PackageId.Value))
        {
            Append(canonical, line.ProductId.Value.ToString("N"));
            Append(canonical, line.PackageId.Value.ToString("N"));
            Append(canonical, line.QuantityPackages.ToString(CultureInfo.InvariantCulture));
            Append(canonical, line.DiscountXof.ToString(CultureInfo.InvariantCulture));
        }
        foreach (SalePaymentRequest payment in request.Payments
                     .OrderBy(item => item.Method)
                     .ThenBy(item => item.Reference?.Trim(), StringComparer.Ordinal)
                     .ThenBy(item => item.AmountXof))
        {
            Append(canonical, ((int)payment.Method).ToString(CultureInfo.InvariantCulture));
            Append(canonical, payment.AmountXof.ToString(CultureInfo.InvariantCulture));
            Append(canonical, payment.Reference?.Trim() ?? string.Empty);
        }

        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToHexStringLower(digest);
    }

    private static void Append(StringBuilder target, string value) => target
        .Append(value.Length.ToString(CultureInfo.InvariantCulture))
        .Append(':')
        .Append(value)
        .Append('|');
}
