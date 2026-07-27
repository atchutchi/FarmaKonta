namespace Nofarma.Application.Inventory;

public sealed record StockOperationPolicyResult(bool IsAllowed, string? Code);
