using System.Globalization;
using System.Security.Cryptography;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Nofarma.Application.Inventory.Import;

namespace Nofarma.Infrastructure.Import;

public static class OpenXmlInventoryFileReader
{
    public static Task<IReadOnlyList<string>> ListWorksheetsAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using SpreadsheetDocument document = SpreadsheetDocument.Open(filePath, false);
            WorkbookPart workbookPart = GetWorkbookPart(document);
            Workbook workbook = GetWorkbook(workbookPart);
            IReadOnlyList<string> names = workbook.Sheets?
                .Elements<Sheet>()
                .Select(sheet => sheet.Name?.Value ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray() ?? [];
            return Task.FromResult(names);
        }
        catch (InventoryFileException)
        {
            throw;
        }
        catch (Exception exception) when (IsSafePackageFailure(exception))
        {
            throw InvalidWorkbook(exception);
        }
    }

    public static async Task<InventoryFileReadResult> ReadAsync(
        string filePath,
        string? worksheetName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string hash = await CalculateHashAsync(filePath, cancellationToken);
            using SpreadsheetDocument document = SpreadsheetDocument.Open(filePath, false);
            WorkbookPart workbookPart = GetWorkbookPart(document);
            Workbook workbook = GetWorkbook(workbookPart);
            Sheet[] sheets = workbook.Sheets?.Elements<Sheet>().ToArray() ?? [];
            Sheet selectedSheet = SelectSheet(sheets, worksheetName);
            string relationshipId = selectedSheet.Id?.Value
                ?? throw new InventoryFileException(
                    "inventory_workbook_invalid",
                    "O ficheiro Excel não contém uma folha legível.");
            if (workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart)
            {
                throw new InventoryFileException(
                    "inventory_workbook_invalid",
                    "O ficheiro Excel não contém uma folha legível.");
            }

            SharedStringTable? sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
            Stylesheet? styles = workbookPart.WorkbookStylesPart?.Stylesheet;
            (IReadOnlyList<string> Headers, IReadOnlyList<InventoryFileRow> Rows) content =
                ReadWorksheet(worksheetPart, sharedStrings, styles, cancellationToken);

            return new InventoryFileReadResult(
                Path.GetFileName(filePath),
                hash,
                selectedSheet.Name?.Value,
                content.Headers,
                content.Rows);
        }
        catch (InventoryFileException)
        {
            throw;
        }
        catch (Exception exception) when (IsSafePackageFailure(exception))
        {
            throw InvalidWorkbook(exception);
        }
    }

    private static (IReadOnlyList<string> Headers, IReadOnlyList<InventoryFileRow> Rows) ReadWorksheet(
        WorksheetPart worksheetPart,
        SharedStringTable? sharedStrings,
        Stylesheet? styles,
        CancellationToken cancellationToken)
    {
        using OpenXmlReader reader = OpenXmlReader.Create(worksheetPart);
        string[]? headers = null;
        var rows = new List<InventoryFileRow>();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.ElementType != typeof(Row) || !reader.IsStartElement)
            {
                continue;
            }

            if (reader.LoadCurrentElement() is not Row row)
            {
                continue;
            }

            int rowNumber = checked((int)(row.RowIndex?.Value ?? 0U));
            List<string?> cells = ReadCells(row, sharedStrings, styles);
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (headers is null)
            {
                headers = cells.Select(value => value?.Trim() ?? string.Empty).ToArray();
                continue;
            }

            if (rows.Count == InventoryFileReader.MaximumDataRows)
            {
                throw new InventoryFileException(
                    "inventory_row_limit_exceeded",
                    "O ficheiro excede o limite de 50 000 linhas de dados.");
            }

            while (cells.Count < headers.Length)
            {
                cells.Add(null);
            }

            rows.Add(new InventoryFileRow(rowNumber, cells));
        }

        if (headers is null || headers.All(string.IsNullOrWhiteSpace))
        {
            throw new InventoryFileException(
                "inventory_workbook_invalid",
                "A folha seleccionada não contém cabeçalhos válidos.");
        }

        return (headers, rows);
    }

    private static List<string?> ReadCells(
        Row row,
        SharedStringTable? sharedStrings,
        Stylesheet? styles)
    {
        var values = new List<string?>();
        foreach (Cell cell in row.Elements<Cell>())
        {
            int targetIndex = GetColumnIndex(cell.CellReference?.Value);
            while (values.Count < targetIndex)
            {
                values.Add(null);
            }

            values.Add(ReadCellValue(cell, sharedStrings, styles));
        }

        return values;
    }

    private static string? ReadCellValue(
        Cell cell,
        SharedStringTable? sharedStrings,
        Stylesheet? styles)
    {
        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.InnerText;
        }

        string? raw = cell.CellValue?.InnerText;
        if (raw is null)
        {
            return null;
        }

        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out int sharedIndex)
            && sharedStrings is not null
            && sharedIndex >= 0
            && sharedIndex < sharedStrings.ChildElements.Count)
        {
            return sharedStrings.ChildElements[sharedIndex].InnerText;
        }

        if (cell.DataType?.Value == CellValues.Boolean)
        {
            return raw == "1" ? "true" : "false";
        }

        if (IsDateCell(cell, styles)
            && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double serialDate))
        {
            return DateTime.FromOADate(serialDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return raw;
    }

    private static bool IsDateCell(Cell cell, Stylesheet? stylesheet)
    {
        if (stylesheet?.CellFormats is null || cell.StyleIndex?.Value is not uint styleIndex)
        {
            return false;
        }

        CellFormat? format = stylesheet.CellFormats.Elements<CellFormat>().ElementAtOrDefault((int)styleIndex);
        uint numberFormatId = format?.NumberFormatId?.Value ?? 0U;
        if ((numberFormatId >= 14U && numberFormatId <= 22U)
            || (numberFormatId >= 45U && numberFormatId <= 47U))
        {
            return true;
        }

        string? formatCode = stylesheet.NumberingFormats?
            .Elements<NumberingFormat>()
            .FirstOrDefault(item => item.NumberFormatId?.Value == numberFormatId)
            ?.FormatCode?.Value;
        if (string.IsNullOrWhiteSpace(formatCode))
        {
            return false;
        }

        string normalized = formatCode.ToLowerInvariant();
        return normalized.Contains('d') && (normalized.Contains('m') || normalized.Contains('y'));
    }

    private static int GetColumnIndex(string? cellReference)
    {
        if (string.IsNullOrWhiteSpace(cellReference))
        {
            return 0;
        }

        int column = 0;
        foreach (char character in cellReference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            column = checked((column * 26) + (char.ToUpperInvariant(character) - 'A' + 1));
        }

        return Math.Max(0, column - 1);
    }

    private static Sheet SelectSheet(Sheet[] sheets, string? worksheetName)
    {
        if (string.IsNullOrWhiteSpace(worksheetName))
        {
            if (sheets.Length != 1)
            {
                throw new InventoryFileException(
                    "inventory_worksheet_required",
                    "Escolhe explicitamente a folha Excel que contém o inventário.");
            }

            return sheets[0];
        }

        return sheets.FirstOrDefault(sheet => string.Equals(
                sheet.Name?.Value,
                worksheetName,
                StringComparison.Ordinal))
            ?? throw new InventoryFileException(
                "inventory_worksheet_not_found",
                "A folha Excel seleccionada já não está disponível.");
    }

    private static WorkbookPart GetWorkbookPart(SpreadsheetDocument document) =>
        document.WorkbookPart
        ?? throw new InventoryFileException(
            "inventory_workbook_invalid",
            "O ficheiro Excel não contém um livro válido.");

    private static Workbook GetWorkbook(WorkbookPart workbookPart) =>
        workbookPart.Workbook
        ?? throw new InventoryFileException(
            "inventory_workbook_invalid",
            "O ficheiro Excel não contém um livro válido.");

    private static async Task<string> CalculateHashAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static bool IsSafePackageFailure(Exception exception) =>
        exception is OpenXmlPackageException
            or InvalidDataException
            or IOException
            or UnauthorizedAccessException
            or FormatException
            or OverflowException;

    private static InventoryFileException InvalidWorkbook(Exception exception) => new(
        "inventory_workbook_invalid",
        "Não foi possível ler o ficheiro Excel. Confirma que não está protegido ou danificado.",
        exception);
}
