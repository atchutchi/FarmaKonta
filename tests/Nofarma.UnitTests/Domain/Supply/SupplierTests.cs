using Nofarma.Domain.Common;
using Nofarma.Domain.Supply;

namespace Nofarma.UnitTests.Domain.Supply;

public sealed class SupplierTests
{
    [Fact]
    public void CreateRejectsBlankName()
    {
        Assert.Throws<SupplierValidationException>(() => Supplier.Create(
            EntityId.New(),
            EntityId.New(),
            " ",
            taxIdentifier: null,
            phone: null,
            email: null,
            address: null,
            notes: null));
    }

    [Fact]
    public void UpdateTrimsContactData()
    {
        Supplier supplier = CreateSupplier();

        supplier.Update(
            "  Farmácia Grossista  ",
            " 500123456 ",
            " 955000000 ",
            " compras@example.test ",
            " Bissau ",
            " Entrega semanal ");

        Assert.Equal("Farmácia Grossista", supplier.Name);
        Assert.Equal("500123456", supplier.TaxIdentifier);
        Assert.Equal("compras@example.test", supplier.Email);
    }

    [Fact]
    public void DeactivatePreservesSupplierHistoryData()
    {
        Supplier supplier = CreateSupplier();

        supplier.Deactivate();

        Assert.False(supplier.IsActive);
        Assert.Equal("Distribuidora Bissau", supplier.Name);
    }

    private static Supplier CreateSupplier() => Supplier.Create(
        EntityId.New(),
        EntityId.New(),
        "Distribuidora Bissau",
        "500123456",
        "955000000",
        "compras@example.test",
        "Bissau",
        null);
}
