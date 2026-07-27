using System.Collections.ObjectModel;
using Nofarma.Domain.Common;

namespace Nofarma.Domain.Catalog;

public sealed class Product
{
    private readonly List<ProductPackage> _packages = [];
    private readonly List<ProductBarcode> _barcodes = [];
    private readonly ReadOnlyCollection<ProductPackage> _readOnlyPackages;
    private readonly ReadOnlyCollection<ProductBarcode> _readOnlyBarcodes;

    private Product(
        EntityId id,
        EntityId pharmacyId,
        ProductCode code,
        string name,
        EntityId categoryId,
        EntityId basePackageId,
        string baseUnit,
        Money salePrice,
        Money indicativePurchasePrice,
        long? minimumStock,
        ProductType type,
        bool requiresLot,
        bool requiresExpiry)
    {
        ValidateIdentifier(id, "O produto deve ter um identificador.");
        ValidateIdentifier(pharmacyId, "A farmácia deve ter um identificador.");
        ValidateIdentifier(categoryId, "A categoria é obrigatória.");
        ValidateIdentifier(basePackageId, "A unidade base deve ter um identificador.");
        if (string.IsNullOrWhiteSpace(code.Value))
        {
            throw new ProductValidationException("O código interno do produto é obrigatório.");
        }

        ValidateText(name, "O nome comercial é obrigatório.");
        ValidateText(baseUnit, "A unidade base é obrigatória.");
        ValidatePrice(salePrice, "O preço de venda");
        ValidatePrice(indicativePurchasePrice, "O preço de compra");
        if (minimumStock is < 0)
        {
            throw new ProductValidationException("O stock mínimo não pode ser negativo.");
        }

        if (requiresExpiry && !requiresLot)
        {
            throw new ProductValidationException(
                "A validade só pode ser exigida quando o produto também exige lote.");
        }

        Id = id;
        PharmacyId = pharmacyId;
        Code = code;
        Name = name.Trim();
        CategoryId = categoryId;
        BasePackageId = basePackageId;
        BaseUnit = baseUnit.Trim();
        SalePrice = salePrice;
        IndicativePurchasePrice = indicativePurchasePrice;
        MinimumStock = minimumStock;
        Type = type;
        RequiresLot = requiresLot;
        RequiresExpiry = requiresExpiry;
        IsActive = true;
        _packages.Add(ProductPackage.Create(basePackageId, BaseUnit, 1, isBaseUnit: true));
        _readOnlyPackages = _packages.AsReadOnly();
        _readOnlyBarcodes = _barcodes.AsReadOnly();
    }

    public EntityId Id { get; }

    public EntityId PharmacyId { get; }

    public ProductCode Code { get; }

    public string Name { get; }

    public EntityId CategoryId { get; }

    public EntityId BasePackageId { get; }

    public string BaseUnit { get; private set; }

    public Money SalePrice { get; private set; }

    public Money IndicativePurchasePrice { get; private set; }

    public long? MinimumStock { get; private set; }

    public ProductType Type { get; }

    public bool RequiresLot { get; private set; }

    public bool RequiresExpiry { get; private set; }

    public bool IsActive { get; private set; }

    public bool HasMovements { get; private set; }

    public IReadOnlyList<ProductPackage> Packages => _readOnlyPackages;

    public IReadOnlyList<ProductBarcode> Barcodes => _readOnlyBarcodes;

    public static Product CreateMedicine(
        EntityId id,
        EntityId pharmacyId,
        ProductCode code,
        string name,
        EntityId categoryId,
        EntityId basePackageId,
        string baseUnit,
        Money salePrice,
        Money indicativePurchasePrice,
        long? minimumStock = null) =>
        new(
            id,
            pharmacyId,
            code,
            name,
            categoryId,
            basePackageId,
            baseUnit,
            salePrice,
            indicativePurchasePrice,
            minimumStock,
            ProductType.Medicine,
            requiresLot: true,
            requiresExpiry: true);

    public static Product CreateGeneral(
        EntityId id,
        EntityId pharmacyId,
        ProductCode code,
        string name,
        EntityId categoryId,
        EntityId basePackageId,
        string baseUnit,
        Money salePrice,
        Money indicativePurchasePrice,
        long? minimumStock = null,
        bool requiresLot = false,
        bool requiresExpiry = false) =>
        new(
            id,
            pharmacyId,
            code,
            name,
            categoryId,
            basePackageId,
            baseUnit,
            salePrice,
            indicativePurchasePrice,
            minimumStock,
            ProductType.General,
            requiresLot,
            requiresExpiry);

    public void AddPackage(EntityId id, string name, long factorToBaseUnit)
    {
        if (_packages.Any(package => package.Id == id))
        {
            throw new ProductValidationException("A embalagem já pertence ao produto.");
        }

        _packages.Add(ProductPackage.Create(id, name, factorToBaseUnit));
    }

    public void ChangePackageFactor(EntityId packageId, long factorToBaseUnit)
    {
        EnsureConversionsCanChange();
        ProductPackage package = FindPackage(packageId);
        package.ChangeFactor(factorToBaseUnit);
    }

    public void ChangeBaseUnit(string baseUnit)
    {
        EnsureConversionsCanChange();
        ValidateText(baseUnit, "A unidade base é obrigatória.");
        BaseUnit = baseUnit.Trim();
        FindPackage(BasePackageId).Rename(BaseUnit);
    }

    public void AddBarcode(EntityId id, EntityId packageId, string value)
    {
        _ = FindPackage(packageId);
        ProductBarcode barcode = ProductBarcode.Create(id, packageId, value);
        if (_barcodes.Any(existing =>
                string.Equals(existing.Value, barcode.Value, StringComparison.Ordinal)))
        {
            throw new ProductValidationException("O código de barras já pertence ao produto.");
        }

        _barcodes.Add(barcode);
    }

    public void ChangePrices(Money salePrice, Money indicativePurchasePrice)
    {
        ValidatePrice(salePrice, "O preço de venda");
        ValidatePrice(indicativePurchasePrice, "O preço de compra");
        SalePrice = salePrice;
        IndicativePurchasePrice = indicativePurchasePrice;
    }

    public void ChangeMinimumStock(long? minimumStock)
    {
        if (minimumStock is < 0)
        {
            throw new ProductValidationException("O stock mínimo não pode ser negativo.");
        }

        MinimumStock = minimumStock;
    }

    public void ChangeTrackingRequirements(bool requiresLot, bool requiresExpiry)
    {
        if (Type == ProductType.Medicine && (!requiresLot || !requiresExpiry))
        {
            throw new ProductValidationException(
                "Os medicamentos exigem sempre lote e validade.");
        }

        if (requiresExpiry && !requiresLot)
        {
            throw new ProductValidationException(
                "A validade só pode ser exigida quando o produto também exige lote.");
        }

        RequiresLot = requiresLot;
        RequiresExpiry = requiresExpiry;
    }

    public void MarkHasMovements() => HasMovements = true;

    public void Deactivate() => IsActive = false;

    private ProductPackage FindPackage(EntityId packageId) =>
        _packages.FirstOrDefault(package => package.Id == packageId) ??
        throw new ProductValidationException("A embalagem não pertence ao produto.");

    private void EnsureConversionsCanChange()
    {
        if (HasMovements)
        {
            throw new ProductValidationException(
                "As unidades e conversões não podem ser alteradas depois do primeiro movimento.");
        }
    }

    private static void ValidateIdentifier(EntityId id, string message)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ProductValidationException(message);
        }
    }

    private static void ValidateText(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ProductValidationException(message);
        }
    }

    private static void ValidatePrice(Money price, string fieldName)
    {
        if (!string.Equals(price.Currency, "XOF", StringComparison.Ordinal) || price.Amount < 0)
        {
            throw new ProductValidationException($"{fieldName} deve ser um valor XOF não negativo.");
        }
    }
}
