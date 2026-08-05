using System.Globalization;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Identity.Authorization;
using Nofarma.Application.Licensing;
using Nofarma.Domain.Catalog;
using Nofarma.Domain.Common;
using Nofarma.Domain.Identity;

namespace Nofarma.Application.Inventory.Import;

public sealed class InventoryImportService(
    IInventoryFileReader fileReader,
    IInventoryImportStore store,
    IInventoryImportErrorWriter errorWriter,
    AuthorizationService authorization,
    ILicensedOperationPolicy licensedOperationPolicy,
    IUtcClock clock)
{
    public async Task<InventoryImportDraft> CreateDraftAsync(
        LocalSession actor,
        CreateInventoryImportDraftRequest request,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ImportInventory);
        InventoryImportStoreContext context = await GetContextAsync(actor, cancellationToken);
        InventoryFileReadResult file = await fileReader.ReadAsync(
            request.FilePath,
            request.WorksheetName,
            cancellationToken);
        Dictionary<ImportColumn, int> mappings = ResolveMappings(file.Headers, request.ColumnMappings);
        var parsed = file.Rows.Select(row => ParseRow(row, mappings)).ToArray();
        IReadOnlyList<ExistingImportProduct> products = await store.FindProductsAsync(
            context.PharmacyId,
            parsed.Select(item => item.Row.Barcode).Where(NotBlank).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            parsed.Select(item => item.Row.InternalCode).Where(NotBlank).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            cancellationToken);
        IReadOnlyList<InventoryImportDraftRow> rows = MatchAndValidate(parsed, products);
        return await store.SaveDraftAsync(
            context,
            actor.UserId,
            file.FileName,
            file.FileHashSha256,
            file.WorksheetName,
            rows,
            clock.GetCurrentInstant().Value,
            cancellationToken);
    }

    public async Task<InventoryImportConfirmationResult> ConfirmAsync(
        LocalSession actor,
        EntityId importId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ImportInventory);
        if (actor.Role != UserRole.Administrator)
        {
            throw new AuthorizationException("A confirmação do inventário inicial exige um administrador.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("A chave idempotente é obrigatória.", nameof(idempotencyKey));
        }

        InventoryImportStoreContext context = await GetContextAsync(actor, cancellationToken);
        string normalizedKey = idempotencyKey.Trim();
        InventoryImportConfirmationResult? repeated = await store.GetConfirmationResultAsync(
            context.PharmacyId,
            importId,
            normalizedKey,
            cancellationToken);
        if (repeated is not null)
        {
            return repeated;
        }

        LicensedOperationPolicyResult policy = await licensedOperationPolicy.CanCreateAsync(cancellationToken);
        if (!policy.IsAllowed)
        {
            throw new StockOperationBlockedException(policy.Code ?? "LICENSE_OPERATION_BLOCKED");
        }

        return await store.ConfirmAsync(
            context,
            actor.UserId,
            importId,
            normalizedKey,
            clock.GetCurrentInstant().Value,
            cancellationToken);
    }

    public async Task<InventoryImportDraft> CorrectRowAsync(
        LocalSession actor,
        EntityId importId,
        EntityId rowId,
        InventoryImportNormalizedRow correctedData,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ImportInventory);
        InventoryImportStoreContext context = await GetContextAsync(actor, cancellationToken);
        InventoryImportDraft draft = await store.GetDraftAsync(context.PharmacyId, importId, cancellationToken)
            ?? throw new ArgumentException("A importação indicada não existe.", nameof(importId));
        if (draft.Status != InventoryImportStatus.Draft)
        {
            throw new InvalidOperationException("Uma importação confirmada não pode ser alterada.");
        }

        InventoryImportDraftRow original = draft.Rows.SingleOrDefault(row => row.Id == rowId)
            ?? throw new ArgumentException("A linha indicada não existe.", nameof(rowId));
        var errors = ValidateNormalizedRow(original.RowNumber, correctedData);
        IReadOnlyList<ExistingImportProduct> products = await store.FindProductsAsync(
            context.PharmacyId,
            NotBlank(correctedData.Barcode) ? [correctedData.Barcode!] : [],
            NotBlank(correctedData.InternalCode) ? [correctedData.InternalCode!] : [],
            cancellationToken);
        InventoryImportDraftRow validated = MatchAndValidate(
            [(original.RowNumber, correctedData, errors)], products).Single() with
        { Id = rowId };
        return await store.UpdateRowAsync(context.PharmacyId, importId, validated, cancellationToken);
    }

    public async Task ExportErrorsAsync(
        LocalSession actor,
        EntityId importId,
        Stream destination,
        CancellationToken cancellationToken)
    {
        authorization.EnsureAllowed(actor, Capability.ImportInventory);
        InventoryImportStoreContext context = await GetContextAsync(actor, cancellationToken);
        IReadOnlyList<InventoryImportError> errors = await store.GetErrorsAsync(
            context.PharmacyId,
            importId,
            cancellationToken);
        await errorWriter.WriteAsync(destination, errors, cancellationToken);
    }

    private async Task<InventoryImportStoreContext> GetContextAsync(LocalSession actor, CancellationToken token) =>
        await store.GetContextAsync(actor.UserId, token)
        ?? throw new AuthorizationException("A sessão actual deixou de ser válida.");

    private static Dictionary<ImportColumn, int> ResolveMappings(
        IReadOnlyList<string> headers,
        IReadOnlyDictionary<ImportColumn, string> mappings)
    {
        var result = new Dictionary<ImportColumn, int>();
        foreach ((ImportColumn column, string header) in mappings)
        {
            int index = -1;
            for (int candidate = 0; candidate < headers.Count; candidate++)
            {
                if (string.Equals(headers[candidate].Trim(), header.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    index = candidate;
                    break;
                }
            }

            if (index < 0) throw new ArgumentException($"O cabeçalho associado a {column} não existe.");
            result[column] = index;
        }

        foreach (ImportColumn required in new[] { ImportColumn.CommercialName, ImportColumn.BaseUnit, ImportColumn.InitialQuantity })
        {
            if (!result.ContainsKey(required)) throw new ArgumentException($"A coluna {required} é obrigatória.");
        }

        return result;
    }

    private static (int Number, InventoryImportNormalizedRow Row, List<InventoryImportError> Errors) ParseRow(
        InventoryFileRow source,
        Dictionary<ImportColumn, int> mappings)
    {
        string? Value(ImportColumn column) => mappings.TryGetValue(column, out int index) && index < source.Cells.Count
            ? source.Cells[index]?.Trim() : null;
        var errors = new List<InventoryImportError>();
        string name = Value(ImportColumn.CommercialName) ?? string.Empty;
        string baseUnit = Value(ImportColumn.BaseUnit) ?? string.Empty;
        ProductType type = ParseProductType(Value(ImportColumn.ProductType), source.RowNumber, errors);
        long factor = ParseLong(Value(ImportColumn.ConversionFactor), 1, false, "factor", source.RowNumber, errors);
        long purchase = ParseLong(Value(ImportColumn.PurchasePriceXof), 0, true, "preco_compra", source.RowNumber, errors);
        long sale = ParseLong(Value(ImportColumn.SalePriceXof), 0, true, "preco_venda", source.RowNumber, errors);
        long quantity = ParseLong(Value(ImportColumn.InitialQuantity), 0, false, "quantidade", source.RowNumber, errors);
        long? minimum = ParseNullableLong(Value(ImportColumn.MinimumStockBase), "stock_minimo", source.RowNumber, errors);
        DateOnly? expiry = ParseDate(Value(ImportColumn.ExpiryDate), source.RowNumber, errors);
        if (string.IsNullOrWhiteSpace(name)) AddError(errors, source.RowNumber, "nome", name, "required", "O nome comercial é obrigatório.");
        if (string.IsNullOrWhiteSpace(baseUnit)) AddError(errors, source.RowNumber, "unidade_base", baseUnit, "required", "A unidade base é obrigatória.");
        string? lot = Value(ImportColumn.LotNumber);
        if (type == ProductType.Medicine && string.IsNullOrWhiteSpace(lot)) AddError(errors, source.RowNumber, "lote", lot, "medicine_lot_required", "Um medicamento exige lote.");
        if (type == ProductType.Medicine && expiry is null) AddError(errors, source.RowNumber, "validade", null, "medicine_expiry_required", "Um medicamento exige validade.");
        return (source.RowNumber, new InventoryImportNormalizedRow(
            Value(ImportColumn.InternalCode), Value(ImportColumn.Barcode), name,
            Value(ImportColumn.ActiveIngredient), Value(ImportColumn.Dosage), Value(ImportColumn.PharmaceuticalForm),
            Value(ImportColumn.Manufacturer), Value(ImportColumn.Category) ?? "Geral", type, baseUnit,
            Value(ImportColumn.Package) ?? baseUnit, factor, purchase, sale, minimum, quantity, lot, expiry,
            Value(ImportColumn.Supplier), Value(ImportColumn.Tax), ParseBoolean(Value(ImportColumn.RequiresPrescription))), errors);
    }

    private static List<InventoryImportDraftRow> MatchAndValidate(
        (int Number, InventoryImportNormalizedRow Row, List<InventoryImportError> Errors)[] parsed,
        IReadOnlyList<ExistingImportProduct> products)
    {
        var barcodeMap = products.SelectMany(product => product.Barcodes.Select(code => (code, product)))
            .GroupBy(item => item.code, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Select(x => x.product).ToArray(), StringComparer.OrdinalIgnoreCase);
        var codeMap = products.GroupBy(product => product.InternalCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<InventoryImportDraftRow>(parsed.Length);
        foreach (var item in parsed)
        {
            ExistingImportProduct? match = null;
            InventoryImportMatchType matchType = InventoryImportMatchType.NewProduct;
            string? identity = NotBlank(item.Row.Barcode) ? $"B:{item.Row.Barcode}" : NotBlank(item.Row.InternalCode) ? $"C:{item.Row.InternalCode}" : null;
            if (identity is not null && !seen.Add(identity)) AddError(item.Errors, item.Number, "codigo", identity[2..], "duplicate_row", "O mesmo código aparece em mais de uma linha.");
            if (NotBlank(item.Row.Barcode) && barcodeMap.TryGetValue(item.Row.Barcode!, out ExistingImportProduct[]? byBarcode))
            {
                if (byBarcode.Length == 1) { match = byBarcode[0]; matchType = InventoryImportMatchType.Barcode; }
                else AddError(item.Errors, item.Number, "codigo_barras", item.Row.Barcode, "barcode_conflict", "O código de barras é ambíguo.");
            }
            else if (NotBlank(item.Row.InternalCode) && codeMap.TryGetValue(item.Row.InternalCode!, out ExistingImportProduct[]? byCode))
            {
                if (byCode.Length == 1) { match = byCode[0]; matchType = InventoryImportMatchType.InternalCode; }
                else AddError(item.Errors, item.Number, "codigo_interno", item.Row.InternalCode, "code_conflict", "O código interno é ambíguo.");
            }

            if (match is not null
                && NotBlank(item.Row.InternalCode)
                && codeMap.TryGetValue(item.Row.InternalCode!, out ExistingImportProduct[]? codeMatches)
                && codeMatches.Length == 1
                && codeMatches[0].Id != match.Id)
            {
                AddError(item.Errors, item.Number, "codigo_interno", item.Row.InternalCode,
                    "identifier_conflict", "O código de barras e o código interno apontam para produtos diferentes.");
            }

            if (match is not null && item.Row.ConversionFactor != 1)
            {
                AddError(item.Errors, item.Number, "factor", item.Row.ConversionFactor.ToString(CultureInfo.InvariantCulture),
                    "existing_package_review_required", "Confirma a embalagem do produto existente antes de aplicar um factor diferente de 1.");
            }

            result.Add(new InventoryImportDraftRow(EntityId.New(), item.Number, item.Row, matchType, match?.Id,
                item.Errors.Count == 0 ? InventoryImportRowStatus.Valid : InventoryImportRowStatus.Blocked, item.Errors));
        }
        return result;
    }

    private static ProductType ParseProductType(string? value, int row, List<InventoryImportError> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("geral", StringComparison.OrdinalIgnoreCase)) return ProductType.General;
        if (value.Equals("medicamento", StringComparison.OrdinalIgnoreCase)) return ProductType.Medicine;
        AddError(errors, row, "tipo_produto", value, "product_type_invalid", "O tipo deve ser Medicamento ou Geral.");
        return ProductType.General;
    }

    private static List<InventoryImportError> ValidateNormalizedRow(
        int rowNumber,
        InventoryImportNormalizedRow row)
    {
        var errors = new List<InventoryImportError>();
        if (string.IsNullOrWhiteSpace(row.CommercialName)) AddError(errors, rowNumber, "nome", row.CommercialName, "required", "O nome comercial é obrigatório.");
        if (string.IsNullOrWhiteSpace(row.BaseUnit)) AddError(errors, rowNumber, "unidade_base", row.BaseUnit, "required", "A unidade base é obrigatória.");
        if (row.ConversionFactor <= 0) AddError(errors, rowNumber, "factor", row.ConversionFactor.ToString(CultureInfo.InvariantCulture), "positive_integer_required", "O factor deve ser positivo.");
        if (row.InitialQuantity <= 0) AddError(errors, rowNumber, "quantidade", row.InitialQuantity.ToString(CultureInfo.InvariantCulture), "positive_integer_required", "A quantidade deve ser positiva.");
        if (row.PurchasePriceXof < 0 || row.SalePriceXof < 0 || row.MinimumStockBase is < 0) AddError(errors, rowNumber, "valor", null, "non_negative_integer_required", "Preços e stock mínimo não podem ser negativos.");
        if (row.ProductType == ProductType.Medicine && string.IsNullOrWhiteSpace(row.LotNumber)) AddError(errors, rowNumber, "lote", row.LotNumber, "medicine_lot_required", "Um medicamento exige lote.");
        if (row.ProductType == ProductType.Medicine && row.ExpiryDate is null) AddError(errors, rowNumber, "validade", null, "medicine_expiry_required", "Um medicamento exige validade.");
        return errors;
    }

    private static long ParseLong(string? value, long fallback, bool allowZero, string field, int row, List<InventoryImportError> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) && (allowZero ? parsed >= 0 : parsed > 0)) return parsed;
        AddError(errors, row, field, value, "positive_integer_required", "O valor deve ser um número inteiro positivo.");
        return fallback;
    }

    private static long? ParseNullableLong(string? value, string field, int row, List<InventoryImportError> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) && parsed >= 0) return parsed;
        AddError(errors, row, field, value, "non_negative_integer_required", "O valor não pode ser negativo.");
        return null;
    }

    private static DateOnly? ParseDate(string? value, int row, List<InventoryImportError> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed)) return parsed;
        AddError(errors, row, "validade", value, "date_invalid", "A validade não é uma data válida.");
        return null;
    }

    private static bool ParseBoolean(string? value) => value is not null && (value.Equals("sim", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");
    private static bool NotBlank(string? value) => !string.IsNullOrWhiteSpace(value);
    private static void AddError(List<InventoryImportError> errors, int row, string field, string? value, string code, string message) => errors.Add(new(row, field, value, code, message));
}
