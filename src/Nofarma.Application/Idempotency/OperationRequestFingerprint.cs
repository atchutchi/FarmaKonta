using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Nofarma.Application.Inventory;
using Nofarma.Application.Purchasing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;

namespace Nofarma.Application.Idempotency;

public static class OperationRequestFingerprint
{
    public static string ForStockEntry(
        InventoryActorContext context,
        EntityId actorUserId,
        StockEntryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var canonical = new CanonicalWriter("nofarma.inventory.entry.v1");
        AppendActor(canonical, context.PharmacyId, context.DeviceId, actorUserId);
        canonical.Append(request.ProductId);
        canonical.Append(request.QuantityBase);
        canonical.Append((int)request.Type);
        canonical.Append(NormalizeLot(request.LotNumber));
        canonical.Append(NormalizeExpiry(request.Expiry));
        canonical.Append(request.SupplierId);
        canonical.Append(request.OriginCostXof);
        canonical.Append(NormalizeOptional(request.Reason));
        canonical.Append(request.SourceDocumentId);
        return canonical.Complete();
    }

    public static string ForStockAdjustment(
        InventoryActorContext context,
        EntityId actorUserId,
        StockAdjustmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var canonical = new CanonicalWriter("nofarma.inventory.adjustment.v1");
        AppendActor(canonical, context.PharmacyId, context.DeviceId, actorUserId);
        canonical.Append(request.ProductId);
        canonical.Append(request.LotId);
        canonical.Append(request.QuantityBase);
        canonical.Append((int)request.Type);
        canonical.Append(NormalizeOptional(request.Reason));
        return canonical.Complete();
    }

    public static string ForStockCompensation(
        InventoryActorContext context,
        EntityId actorUserId,
        StockCompensationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var canonical = new CanonicalWriter("nofarma.inventory.compensation.v1");
        AppendActor(canonical, context.PharmacyId, context.DeviceId, actorUserId);
        canonical.Append(request.OriginalMovementId);
        canonical.Append(NormalizeOptional(request.Reason));
        return canonical.Complete();
    }

    public static string ForPurchaseReceipt(
        PurchaseActorContext context,
        EntityId purchaseId,
        EntityId actorUserId,
        ConfirmPurchaseReceiptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var canonical = new CanonicalWriter("nofarma.purchase.receipt.v1");
        AppendActor(canonical, context.PharmacyId, context.DeviceId, actorUserId);
        canonical.Append(purchaseId);
        canonical.Append(request.DocumentNumber?.Trim());
        canonical.Append(request.DocumentDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        canonical.Append(NormalizeOptional(request.Notes));
        ConfirmPurchaseReceiptLineRequest[] lines = request.Lines
            .OrderBy(line => line.PurchaseOrderLineId.Value)
            .ToArray();
        canonical.Append(lines.Length);
        foreach (ConfirmPurchaseReceiptLineRequest line in lines)
        {
            canonical.Append(line.PurchaseOrderLineId);
            canonical.Append(line.PackageQuantity);
            canonical.Append(line.UnitCostXof);
            canonical.Append(NormalizeLot(line.LotNumber));
            canonical.Append(NormalizeExpiry(line.Expiry));
        }

        return canonical.Complete();
    }

    public static bool MatchesPersisted(string? persisted, string expected)
    {
        byte[]? persistedBytes = DecodeSha256(persisted);
        byte[]? expectedBytes = DecodeSha256(expected);
        return persistedBytes is not null &&
            expectedBytes is not null &&
            CryptographicOperations.FixedTimeEquals(persistedBytes, expectedBytes);
    }

    private static void AppendActor(
        CanonicalWriter canonical,
        EntityId pharmacyId,
        EntityId deviceId,
        EntityId actorUserId)
    {
        canonical.Append(pharmacyId);
        canonical.Append(deviceId);
        canonical.Append(actorUserId);
    }

    private static string NormalizeLot(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "SEM-LOTE" : value.Trim().ToUpperInvariant();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeExpiry(ExpiryDate? expiry) => expiry switch
    {
        null => null,
        { Day: int day } value =>
            $"{value.Year.ToString("D4", CultureInfo.InvariantCulture)}-" +
            $"{value.Month.ToString("D2", CultureInfo.InvariantCulture)}-" +
            day.ToString("D2", CultureInfo.InvariantCulture),
        { } value =>
            $"{value.Year.ToString("D4", CultureInfo.InvariantCulture)}-" +
            value.Month.ToString("D2", CultureInfo.InvariantCulture)
    };

    private static byte[]? DecodeSha256(string? value)
    {
        if (value is null || value.Length != 64)
        {
            return null;
        }

        try
        {
            byte[] bytes = Convert.FromHexString(value);
            return bytes.Length == SHA256.HashSizeInBytes ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed class CanonicalWriter : IDisposable
    {
        private readonly MemoryStream _stream = new();

        public CanonicalWriter(string operation)
        {
            Append(operation);
        }

        public void Append(EntityId value) => Append(value.Value.ToString("N"));

        public void Append(EntityId? value) =>
            Append(value?.Value.ToString("N"));

        public void Append(long value) =>
            Append(value.ToString(CultureInfo.InvariantCulture));

        public void Append(int value) =>
            Append(value.ToString(CultureInfo.InvariantCulture));

        public void Append(string? value)
        {
            if (value is null)
            {
                Span<byte> nullLength = stackalloc byte[sizeof(int)];
                BinaryPrimitives.WriteInt32BigEndian(nullLength, -1);
                _stream.Write(nullLength);
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            _stream.Write(length);
            _stream.Write(bytes);
        }

        public string Complete() => Convert.ToHexString(SHA256.HashData(_stream.ToArray()));

        public void Dispose() => _stream.Dispose();
    }
}
