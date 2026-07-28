using Nofarma.Application.Abstractions;
using Nofarma.Application.Identity.Authentication;
using Nofarma.Application.Inventory;
using Nofarma.Application.Inventory.Import;
using Nofarma.Domain.Common;

namespace Nofarma.Desktop.ViewModels;

public enum InventoryImportStep
{
    File = 1,
    Mapping = 2,
    Validation = 3,
    Confirmation = 4
}

public sealed record InventoryImportFilePreview(
    string FilePath,
    string? WorksheetName,
    IReadOnlyList<string> Worksheets,
    IReadOnlyList<string> Headers);

public interface IInventoryImportPageOperations
{
    Task<InventoryImportFilePreview> InspectAsync(string filePath, string? worksheetName, CancellationToken cancellationToken);
    Task<InventoryImportDraft> CreateDraftAsync(string filePath, string? worksheetName, IReadOnlyDictionary<ImportColumn, string> mappings, CancellationToken cancellationToken);
    Task<InventoryImportConfirmationResult> ConfirmAsync(EntityId importId, string idempotencyKey, CancellationToken cancellationToken);
}

public sealed class InventoryImportPageOperations(
    IInventoryFileReader fileReader,
    InventoryImportService importService,
    CurrentSession currentSession) : IInventoryImportPageOperations
{
    public async Task<InventoryImportFilePreview> InspectAsync(
        string filePath,
        string? worksheetName,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> worksheets = await fileReader.ListWorksheetsAsync(filePath, cancellationToken);
        string? selectedWorksheet = string.IsNullOrWhiteSpace(worksheetName)
            ? worksheets.Count > 0 ? worksheets[0] : null
            : worksheetName;
        InventoryFileReadResult file = await fileReader.ReadAsync(filePath, selectedWorksheet, cancellationToken);
        return new InventoryImportFilePreview(filePath, file.WorksheetName, worksheets, file.Headers);
    }

    public Task<InventoryImportDraft> CreateDraftAsync(
        string filePath,
        string? worksheetName,
        IReadOnlyDictionary<ImportColumn, string> mappings,
        CancellationToken cancellationToken) =>
        importService.CreateDraftAsync(
            ActiveSession(),
            new CreateInventoryImportDraftRequest(filePath, worksheetName, mappings),
            cancellationToken);

    public Task<InventoryImportConfirmationResult> ConfirmAsync(
        EntityId importId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        importService.ConfirmAsync(ActiveSession(), importId, idempotencyKey, cancellationToken);

    private LocalSession ActiveSession() => currentSession.Active
        ?? throw new InvalidOperationException("Não existe uma sessão activa.");
}

public sealed class InventoryImportViewModel(IInventoryImportPageOperations operations)
{
    private string _confirmationKey = Guid.NewGuid().ToString("N");

    public InventoryImportStep Step { get; private set; } = InventoryImportStep.File;
    public string FilePath { get; private set; } = string.Empty;
    public string? SelectedWorksheet { get; private set; }
    public IReadOnlyList<string> Worksheets { get; private set; } = [];
    public IReadOnlyList<string> Headers { get; private set; } = [];
    public Dictionary<ImportColumn, string> ColumnMappings { get; } = [];
    public InventoryImportDraft? Draft { get; private set; }
    public InventoryImportConfirmationResult? Result { get; private set; }
    public bool IsBusy { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task<bool> PrepareFileAsync(string filePath, string? worksheetName, CancellationToken cancellationToken)
    {
        if (IsBusy) return false;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            ErrorMessage = "Selecciona um ficheiro Excel ou CSV.";
            return false;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            InventoryImportFilePreview preview = await operations.InspectAsync(filePath.Trim(), worksheetName, cancellationToken);
            FilePath = preview.FilePath;
            SelectedWorksheet = preview.WorksheetName;
            Worksheets = preview.Worksheets;
            Headers = preview.Headers;
            AutoMapKnownHeaders();
            Step = InventoryImportStep.Mapping;
            return true;
        }
        catch (InventoryFileException exception)
        {
            ErrorMessage = exception.Message;
            return false;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "Não foi possível ler o ficheiro. Confirma o formato e tenta novamente.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<bool> ChangeWorksheetAsync(string worksheetName, CancellationToken cancellationToken) =>
        PrepareFileAsync(FilePath, worksheetName, cancellationToken);

    public void SetMapping(ImportColumn column, string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) ColumnMappings.Remove(column);
        else ColumnMappings[column] = header.Trim();
    }

    public async Task<bool> CreateDraftAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return false;
        if (!HasRequiredMappings())
        {
            ErrorMessage = "Associa as colunas Nome comercial, Unidade base e Quantidade inicial.";
            return false;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            Draft = await operations.CreateDraftAsync(FilePath, SelectedWorksheet, ColumnMappings, cancellationToken);
            Step = InventoryImportStep.Validation;
            return true;
        }
        catch (InventoryFileException exception)
        {
            ErrorMessage = exception.Message;
            return false;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "Não foi possível preparar o rascunho. Revê o mapeamento e tenta novamente.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool ContinueToConfirmation()
    {
        if (Draft is null || Draft.ErrorRows > 0)
        {
            ErrorMessage = "Corrige as linhas bloqueadas antes de confirmar o inventário.";
            return false;
        }

        ErrorMessage = null;
        Step = InventoryImportStep.Confirmation;
        return true;
    }

    public async Task<bool> ConfirmAsync(bool explicitlyConfirmed, CancellationToken cancellationToken)
    {
        if (IsBusy || Draft is null) return false;
        if (!explicitlyConfirmed)
        {
            ErrorMessage = "Confirma que verificaste os dados antes de aplicar o inventário.";
            return false;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            Result = await operations.ConfirmAsync(Draft.Id, _confirmationKey, cancellationToken);
            _confirmationKey = Guid.NewGuid().ToString("N");
            return true;
        }
        catch (StockOperationBlockedException)
        {
            ErrorMessage = "A licença deve estar activa para confirmar. O rascunho continua guardado neste computador.";
            return false;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = "Não foi possível confirmar o inventário. O rascunho continua guardado.";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void GoBack()
    {
        ErrorMessage = null;
        Step = Step switch
        {
            InventoryImportStep.Mapping => InventoryImportStep.File,
            InventoryImportStep.Validation => InventoryImportStep.Mapping,
            InventoryImportStep.Confirmation => InventoryImportStep.Validation,
            _ => InventoryImportStep.File
        };
    }

    private bool HasRequiredMappings() =>
        ColumnMappings.ContainsKey(ImportColumn.CommercialName) &&
        ColumnMappings.ContainsKey(ImportColumn.BaseUnit) &&
        ColumnMappings.ContainsKey(ImportColumn.InitialQuantity);

    private void AutoMapKnownHeaders()
    {
        Map(ImportColumn.InternalCode, "Código interno", "Codigo interno", "codigo_interno");
        Map(ImportColumn.Barcode, "Código de barras", "Codigo de barras", "codigo_barras");
        Map(ImportColumn.CommercialName, "Nome comercial", "Nome", "nome");
        Map(ImportColumn.ProductType, "Tipo", "tipo_produto");
        Map(ImportColumn.BaseUnit, "Unidade base", "Unidade", "unidade_base");
        Map(ImportColumn.Package, "Embalagem", "embalagem");
        Map(ImportColumn.ConversionFactor, "Factor", "fator", "factor_conversao");
        Map(ImportColumn.PurchasePriceXof, "Preço de compra", "preco_compra");
        Map(ImportColumn.SalePriceXof, "Preço de venda", "preco_venda");
        Map(ImportColumn.MinimumStockBase, "Stock mínimo", "stock_minimo");
        Map(ImportColumn.InitialQuantity, "Quantidade inicial", "Quantidade", "quantidade");
        Map(ImportColumn.LotNumber, "Lote", "lote");
        Map(ImportColumn.ExpiryDate, "Validade", "validade");
        Map(ImportColumn.Supplier, "Fornecedor", "fornecedor");
    }

    private void Map(ImportColumn column, params string[] aliases)
    {
        if (ColumnMappings.ContainsKey(column)) return;
        string? header = Headers.FirstOrDefault(candidate => aliases.Any(alias =>
            string.Equals(candidate.Trim(), alias, StringComparison.OrdinalIgnoreCase)));
        if (header is not null) ColumnMappings[column] = header;
    }
}
