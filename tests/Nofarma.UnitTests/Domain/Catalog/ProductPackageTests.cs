using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;

namespace Nofarma.UnitTests.Domain.Catalog;

public sealed class ProductPackageTests
{
    [Fact]
    public void AddPackageRejectsZeroConversionFactor()
    {
        Product product = CreateGeneralProduct();

        Assert.Throws<ProductValidationException>(() =>
            product.AddPackage(EntityId.New(), "Caixa", 0));
    }

    [Fact]
    public void BasePackageAlwaysRepresentsOneBaseUnit()
    {
        Product product = CreateGeneralProduct();

        ProductPackage basePackage = Assert.Single(product.Packages);
        Assert.Equal(1, basePackage.FactorToBaseUnit);
        Assert.True(basePackage.IsBaseUnit);
    }

    [Fact]
    public void UsedConversionCannotBeChangedAfterFirstMovement()
    {
        Product product = CreateGeneralProduct();
        EntityId packageId = EntityId.New();
        product.AddPackage(packageId, "Caixa", 12);
        product.MarkHasMovements();

        Assert.Throws<ProductValidationException>(() =>
            product.ChangePackageFactor(packageId, 24));
    }

    [Fact]
    public void BaseUnitCannotBeChangedAfterFirstMovement()
    {
        Product product = CreateGeneralProduct();
        product.MarkHasMovements();

        Assert.Throws<ProductValidationException>(() =>
            product.ChangeBaseUnit("Barra"));
    }

    private static Product CreateGeneralProduct() => Product.CreateGeneral(
        EntityId.New(),
        EntityId.New(),
        ProductCode.Parse("SAB-001"),
        "Sabonete",
        EntityId.New(),
        EntityId.New(),
        "Unidade",
        Money.Xof(500),
        Money.Xof(300));
}
