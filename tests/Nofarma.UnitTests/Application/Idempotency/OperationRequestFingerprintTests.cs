using Nofarma.Application.Idempotency;
using Nofarma.Application.Inventory;
using Nofarma.Application.Purchasing;
using Nofarma.Domain.Common;
using Nofarma.Domain.Inventory;

namespace Nofarma.UnitTests.Application.Idempotency;

public sealed class OperationRequestFingerprintTests
{
    private static readonly EntityId PharmacyId = Id("11111111-1111-1111-1111-111111111111");
    private static readonly EntityId DeviceId = Id("22222222-2222-2222-2222-222222222222");
    private static readonly EntityId UserId = Id("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void StockEntryUsesCanonicalNormalizedIntent()
    {
        var context = new InventoryActorContext(PharmacyId, DeviceId);
        var request = new StockEntryRequest(
            Id("44444444-4444-4444-4444-444444444444"),
            10,
            StockMovementType.QuickEntry,
            " lot-á ",
            ExpiryDate.ForMonth(2027, 12),
            null,
            50,
            " Entrada ",
            null,
            "ignored-key");

        string fingerprint = OperationRequestFingerprint.ForStockEntry(
            context,
            UserId,
            request);

        Assert.Equal(
            "A87ED97CA8E8377DB7BEAF6803C021111E405E001DBD8A02D2BA9D4A12A995F7",
            fingerprint);
    }

    [Fact]
    public void PurchaseReceiptUsesCanonicalLineOrderAndNormalizedIntent()
    {
        var context = new PurchaseActorContext(PharmacyId, DeviceId, UserId);
        var request = new ConfirmPurchaseReceiptRequest(
            " FT-2026-001 ",
            new DateOnly(2026, 7, 28),
            " Nota ",
            "ignored-key",
            [
                new(
                    Id("77777777-7777-7777-7777-777777777777"),
                    4,
                    900,
                    " lot-a ",
                    ExpiryDate.ForMonth(2027, 12)),
                new(
                    Id("66666666-6666-6666-6666-666666666666"),
                    2,
                    1_000,
                    " lot-b ",
                    ExpiryDate.ForDay(2028, 1, 15))
            ]);

        string fingerprint = OperationRequestFingerprint.ForPurchaseReceipt(
            context,
            Id("55555555-5555-5555-5555-555555555555"),
            UserId,
            request);

        Assert.Equal(
            "DD12EFA89CA860E86C625DE41E4666B1F2B6262B9C3E6D2B28C8AE8ABBFB7DF6",
            fingerprint);
    }

    [Fact]
    public void StockAdjustmentUsesCanonicalNormalizedIntent()
    {
        var request = new StockAdjustmentRequest(
            Id("44444444-4444-4444-4444-444444444444"),
            Id("55555555-5555-5555-5555-555555555555"),
            -3,
            StockMovementType.Loss,
            " Quebra ",
            "ignored-key");

        string fingerprint = OperationRequestFingerprint.ForStockAdjustment(
            new InventoryActorContext(PharmacyId, DeviceId),
            UserId,
            request);

        Assert.Equal(
            "D4CCA898F76AB0088B3D9ED1D50D4BADC86CA88FD7BD849009EF2F8AED3176AA",
            fingerprint);
    }

    [Fact]
    public void StockCompensationUsesCanonicalNormalizedIntent()
    {
        var request = new StockCompensationRequest(
            Id("66666666-6666-6666-6666-666666666666"),
            " Erro ",
            "ignored-key");

        string fingerprint = OperationRequestFingerprint.ForStockCompensation(
            new InventoryActorContext(PharmacyId, DeviceId),
            UserId,
            request);

        Assert.Equal(
            "D144C8DBBA0B6DC77BDEE1E5EE9B77DE06CFC7514F0152F2B3F009F5AF3150CF",
            fingerprint);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("NOT-A-SHA256", false)]
    [InlineData("A87ED97CA8E8377DB7BEAF6803C021111E405E001DBD8A02D2BA9D4A12A995F7", true)]
    [InlineData("a87ed97ca8e8377db7beaf6803c021111e405e001dbd8a02d2ba9d4a12a995f7", true)]
    public void PersistedFingerprintMustBeACompleteMatchingSha256(
        string? persisted,
        bool expected)
    {
        Assert.Equal(
            expected,
            OperationRequestFingerprint.MatchesPersisted(
                persisted,
                "A87ED97CA8E8377DB7BEAF6803C021111E405E001DBD8A02D2BA9D4A12A995F7"));
    }

    private static EntityId Id(string value) => new(Guid.Parse(value));
}
