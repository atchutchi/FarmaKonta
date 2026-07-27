using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;

namespace Nofarma.UnitTests.Domain.Catalog;

public sealed class ProductTests
{
    [Fact]
    public void MedicineAlwaysRequiresLotAndExpiry()
    {
        Product product = CreateMedicine();

        Assert.Equal(ProductType.Medicine, product.Type);
        Assert.True(product.RequiresLot);
        Assert.True(product.RequiresExpiry);
    }

    [Fact]
    public void GeneralProductCanRequireLotWithoutExpiry()
    {
        Product product = Product.CreateGeneral(
            EntityId.New(),
            EntityId.New(),
            ProductCode.Parse("COS-001"),
            "Creme",
            EntityId.New(),
            EntityId.New(),
            "Tubo",
            Money.Xof(2_000),
            Money.Xof(1_300),
            minimumStock: 5,
            requiresLot: true,
            requiresExpiry: false);

        Assert.True(product.RequiresLot);
        Assert.False(product.RequiresExpiry);
        Assert.Equal(5, product.MinimumStock);
    }

    [Fact]
    public void GeneralProductCanEnableLotAndExpiryAfterCreation()
    {
        Product product = Product.CreateGeneral(
            EntityId.New(),
            EntityId.New(),
            ProductCode.Parse("COS-002"),
            "Loção",
            EntityId.New(),
            EntityId.New(),
            "Frasco",
            Money.Xof(2_500),
            Money.Xof(1_500));

        product.ChangeTrackingRequirements(requiresLot: true, requiresExpiry: true);

        Assert.True(product.RequiresLot);
        Assert.True(product.RequiresExpiry);
    }

    [Fact]
    public void MedicineCannotDisableLotOrExpiry()
    {
        Product product = CreateMedicine();

        Assert.Throws<ProductValidationException>(() =>
            product.ChangeTrackingRequirements(requiresLot: false, requiresExpiry: false));
    }

    [Fact]
    public void ExpiryRequirementCannotExistWithoutLotRequirement()
    {
        Product product = Product.CreateGeneral(
            EntityId.New(),
            EntityId.New(),
            ProductCode.Parse("COS-003"),
            "Gel",
            EntityId.New(),
            EntityId.New(),
            "Frasco",
            Money.Xof(1_500),
            Money.Xof(900));

        Assert.Throws<ProductValidationException>(() =>
            product.ChangeTrackingRequirements(requiresLot: false, requiresExpiry: true));
    }

    [Fact]
    public void GeneralProductCannotStartWithExpiryWithoutLot()
    {
        Assert.Throws<ProductValidationException>(() => Product.CreateGeneral(
            EntityId.New(),
            EntityId.New(),
            ProductCode.Parse("COS-004"),
            "Champô",
            EntityId.New(),
            EntityId.New(),
            "Frasco",
            Money.Xof(1_500),
            Money.Xof(900),
            requiresLot: false,
            requiresExpiry: true));
    }

    [Fact]
    public void CreateRejectsBlankName()
    {
        Assert.Throws<ProductValidationException>(() => Product.CreateGeneral(
            EntityId.New(),
            EntityId.New(),
            ProductCode.Parse("GEN-001"),
            " ",
            EntityId.New(),
            EntityId.New(),
            "Unidade",
            Money.Xof(100),
            Money.Xof(50)));
    }

    [Fact]
    public void CreateRejectsNegativePrice()
    {
        Assert.Throws<ProductValidationException>(() => Product.CreateGeneral(
            EntityId.New(),
            EntityId.New(),
            ProductCode.Parse("GEN-001"),
            "Produto",
            EntityId.New(),
            EntityId.New(),
            "Unidade",
            Money.Xof(-1),
            Money.Xof(50)));
    }

    [Fact]
    public void CreateRejectsNegativeMinimumStock()
    {
        Assert.Throws<ProductValidationException>(() => Product.CreateGeneral(
            EntityId.New(),
            EntityId.New(),
            ProductCode.Parse("GEN-001"),
            "Produto",
            EntityId.New(),
            EntityId.New(),
            "Unidade",
            Money.Xof(100),
            Money.Xof(50),
            minimumStock: -1));
    }

    [Fact]
    public void CreateRejectsPriceOutsideXof()
    {
        Assert.Throws<ProductValidationException>(() => Product.CreateGeneral(
            EntityId.New(),
            EntityId.New(),
            ProductCode.Parse("GEN-001"),
            "Produto",
            EntityId.New(),
            EntityId.New(),
            "Unidade",
            new Money(100, "EUR"),
            Money.Xof(50)));
    }

    [Fact]
    public void CreateRejectsUninitializedProductCode()
    {
        Assert.Throws<ProductValidationException>(() => Product.CreateGeneral(
            EntityId.New(),
            EntityId.New(),
            default,
            "Produto",
            EntityId.New(),
            EntityId.New(),
            "Unidade",
            Money.Xof(100),
            Money.Xof(50)));
    }

    [Fact]
    public void ChangePricesReplacesBothXofValues()
    {
        Product product = CreateMedicine();

        product.ChangePrices(Money.Xof(125), Money.Xof(75));

        Assert.Equal(125, product.SalePrice.Amount);
        Assert.Equal(75, product.IndicativePurchasePrice.Amount);
    }

    [Fact]
    public void ChangeMinimumStockRejectsNegativeValue()
    {
        Product product = CreateMedicine();

        Assert.Throws<ProductValidationException>(() => product.ChangeMinimumStock(-1));
    }

    [Fact]
    public void AddBarcodeAllowsSeveralPackagesAndRejectsDuplicateValue()
    {
        Product product = CreateMedicine();
        EntityId boxId = EntityId.New();
        product.AddPackage(boxId, "Caixa", 100);

        product.AddBarcode(EntityId.New(), product.BasePackageId, "5600000000011");
        product.AddBarcode(EntityId.New(), boxId, "5600000000028");

        Assert.Equal(2, product.Barcodes.Count);
        Assert.Throws<ProductValidationException>(() =>
            product.AddBarcode(EntityId.New(), boxId, "5600000000011"));
    }

    [Fact]
    public void DeactivatePreservesProductData()
    {
        Product product = CreateMedicine();

        product.Deactivate();

        Assert.False(product.IsActive);
        Assert.Equal("Paracetamol 500 mg", product.Name);
        Assert.NotEmpty(product.Packages);
    }

    [Fact]
    public void BarcodeMustReferenceAnExistingPackage()
    {
        Product product = CreateMedicine();

        Assert.Throws<ProductValidationException>(() =>
            product.AddBarcode(EntityId.New(), EntityId.New(), "5600000000011"));
    }

    private static Product CreateMedicine() => Product.CreateMedicine(
        EntityId.New(),
        EntityId.New(),
        ProductCode.Parse("MED-001"),
        "Paracetamol 500 mg",
        EntityId.New(),
        EntityId.New(),
        "Comprimido",
        Money.Xof(100),
        Money.Xof(60),
        minimumStock: 10);
}
