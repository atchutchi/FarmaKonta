using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;

namespace Nofarma.UnitTests.Domain.Catalog;

public sealed class ProductCategoryTests
{
    [Fact]
    public void CreateRejectsBlankName()
    {
        Assert.Throws<ProductValidationException>(() =>
            ProductCategory.Create(EntityId.New(), EntityId.New(), " "));
    }

    [Fact]
    public void DeactivatePreservesIdentityAndName()
    {
        EntityId id = EntityId.New();
        ProductCategory category = ProductCategory.Create(id, EntityId.New(), "Medicamentos");

        category.Deactivate();

        Assert.Equal(id, category.Id);
        Assert.Equal("Medicamentos", category.Name);
        Assert.False(category.IsActive);
    }
}
