using Nofarma.Domain.Common;

namespace Nofarma.Domain.Sales;

public sealed class Receipt
{
    public const string InternalNonFiscalLabel = "Recibo interno não fiscal";

    private Receipt(
        EntityId id,
        Sale sale,
        string pharmacyName,
        string operatorName,
        UtcInstant createdAt)
    {
        Id = id;
        SaleId = sale.Id;
        PharmacyId = sale.PharmacyId;
        Number = sale.Number;
        PharmacyName = pharmacyName;
        OperatorName = operatorName;
        DocumentLabel = InternalNonFiscalLabel;
        Lines = Array.AsReadOnly(sale.Lines.ToArray());
        Payments = Array.AsReadOnly(sale.Payments.ToArray());
        Total = sale.Total;
        Change = sale.Change;
        CreatedAt = createdAt;
    }

    public EntityId Id { get; }
    public EntityId SaleId { get; }
    public EntityId PharmacyId { get; }
    public SaleNumber Number { get; }
    public string PharmacyName { get; }
    public string OperatorName { get; }
    public string DocumentLabel { get; }
    public IReadOnlyList<SaleLine> Lines { get; }
    public IReadOnlyList<SalePayment> Payments { get; }
    public Money Total { get; }
    public Money Change { get; }
    public UtcInstant CreatedAt { get; }

    public static Receipt Create(
        EntityId id,
        Sale sale,
        string pharmacyName,
        string operatorName,
        UtcInstant createdAt)
    {
        if (id.Value == Guid.Empty)
        {
            throw new SalesValidationException("O recibo deve ter um identificador.");
        }
        ArgumentNullException.ThrowIfNull(sale);
        string normalizedPharmacyName = NormalizeName(pharmacyName, "A farmácia");
        string normalizedOperatorName = NormalizeName(operatorName, "O operador");
        if (createdAt == default || createdAt.Value < sale.CompletedAt.Value)
        {
            throw new SalesValidationException(
                "A data do recibo não pode ser anterior à conclusão da venda.");
        }

        return new Receipt(id, sale, normalizedPharmacyName, normalizedOperatorName, createdAt);
    }

    private static string NormalizeName(string value, string field)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > 240)
        {
            throw new SalesValidationException(
                $"{field} do recibo é obrigatória e não pode exceder 240 caracteres.");
        }
        return normalized;
    }
}
