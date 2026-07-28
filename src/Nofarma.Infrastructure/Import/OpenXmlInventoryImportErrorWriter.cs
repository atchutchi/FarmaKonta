using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Nofarma.Application.Abstractions;
using Nofarma.Application.Inventory.Import;

namespace Nofarma.Infrastructure.Import;

public sealed class OpenXmlInventoryImportErrorWriter : IInventoryImportErrorWriter
{
    public Task WriteAsync(Stream destination, IReadOnlyList<InventoryImportError> errors, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using SpreadsheetDocument document = SpreadsheetDocument.Create(destination, SpreadsheetDocumentType.Workbook, autoSave: true);
        WorkbookPart workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        WorksheetPart worksheet = workbook.AddNewPart<WorksheetPart>();
        var data = new SheetData();
        data.Append(RowOf("Linha", "Campo", "Valor recebido", "Código", "Explicação"));
        foreach (InventoryImportError error in errors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            data.Append(RowOf(error.RowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Field, error.ReceivedValue, error.Code, error.Message));
        }
        worksheet.Worksheet = new Worksheet(data);
        var sheets = new Sheets();
        sheets.Append(new Sheet { Id = workbook.GetIdOfPart(worksheet), SheetId = 1U, Name = "Erros" });
        workbook.Workbook.Append(sheets);
        workbook.Workbook.Save();
        return Task.CompletedTask;
    }

    private static Row RowOf(params string?[] values) => new(values.Select(value => new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value ?? string.Empty)) }));
}
