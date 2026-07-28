using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Nofarma.Application.Inventory.Import;
using Nofarma.Infrastructure.Import;

namespace Nofarma.IntegrationTests.Inventory;

public sealed class InventoryFileReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"nofarma-import-{Guid.NewGuid():N}");

    public InventoryFileReaderTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task ReadAsyncRejectsUnsupportedExtensionWithoutExposingPath()
    {
        string path = WriteFile("inventory.txt", "nome,quantidade\nProduto,1");
        var reader = new InventoryFileReader();

        InventoryFileException exception = await Assert.ThrowsAsync<InventoryFileException>(
            () => reader.ReadAsync(path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("inventory_file_type_invalid", exception.Code);
        Assert.DoesNotContain(_directory, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsyncRejectsFileLargerThanTwentyMebibytes()
    {
        string path = Path.Combine(_directory, "large.csv");
        await using (FileStream stream = File.Create(path))
        {
            stream.SetLength((20L * 1024 * 1024) + 1);
        }

        var reader = new InventoryFileReader();

        InventoryFileException exception = await Assert.ThrowsAsync<InventoryFileException>(
            () => reader.ReadAsync(path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("inventory_file_too_large", exception.Code);
    }

    [Theory]
    [InlineData(',', "Ibuprofeno")]
    [InlineData(';', "Ácido fólico")]
    public async Task ReadAsyncReadsUtf8CsvAndDetectsSupportedDelimiter(
        char delimiter,
        string productName)
    {
        string content = $"código{delimiter}nome{delimiter}quantidade\nP-001{delimiter}{productName}{delimiter}12";
        string path = WriteFile("inventory.csv", content);
        var reader = new InventoryFileReader();

        InventoryFileReadResult result = await reader.ReadAsync(
            path,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["código", "nome", "quantidade"], result.Headers);
        InventoryFileRow row = Assert.Single(result.Rows);
        Assert.Equal(2, row.RowNumber);
        Assert.Equal(["P-001", productName, "12"], row.Cells);
        Assert.Equal("inventory.csv", result.FileName);
        Assert.Matches("^[A-F0-9]{64}$", result.FileHashSha256);
    }

    [Fact]
    public async Task ReadAsyncRejectsMoreThanFiftyThousandDataRows()
    {
        string path = Path.Combine(_directory, "rows.csv");
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            await writer.WriteLineAsync("nome,quantidade");
            for (int row = 0; row < 50_001; row++)
            {
                await writer.WriteLineAsync($"Produto {row},1");
            }
        }

        var reader = new InventoryFileReader();

        InventoryFileException exception = await Assert.ThrowsAsync<InventoryFileException>(
            () => reader.ReadAsync(path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("inventory_row_limit_exceeded", exception.Code);
    }

    [Fact]
    public async Task ListWorksheetsAsyncReturnsWorkbookSheetsAndRequiresSelection()
    {
        string path = CreateWorkbook("inventory.xlsx", includeSecondSheet: true);
        var reader = new InventoryFileReader();

        IReadOnlyList<string> sheets = await reader.ListWorksheetsAsync(
            path,
            TestContext.Current.CancellationToken);
        InventoryFileException exception = await Assert.ThrowsAsync<InventoryFileException>(
            () => reader.ReadAsync(path, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(["Instruções", "Inventário"], sheets);
        Assert.Equal("inventory_worksheet_required", exception.Code);
    }

    [Fact]
    public async Task ReadAsyncReadsSelectedWorksheetExcelDateAndCachedFormulaValue()
    {
        string path = CreateWorkbook("inventory.xlsx", includeSecondSheet: true);
        var reader = new InventoryFileReader();

        InventoryFileReadResult result = await reader.ReadAsync(
            path,
            "Inventário",
            TestContext.Current.CancellationToken);

        Assert.Equal("Inventário", result.WorksheetName);
        Assert.Equal(["produto", "validade", "quantidade"], result.Headers);
        InventoryFileRow row = Assert.Single(result.Rows);
        Assert.Equal(["Paracetamol", "2027-12-31", "2"], row.Cells);
    }

    [Fact]
    public async Task ErrorWriterCreatesLocalWorkbookWithoutTechnicalMetadata()
    {
        var writer = new OpenXmlInventoryImportErrorWriter();
        await using var output = new MemoryStream();

        await writer.WriteAsync(
            output,
            [new InventoryImportError(7, "quantidade", "-2", "positive_integer_required", "O valor deve ser positivo.")],
            TestContext.Current.CancellationToken);

        output.Position = 0;
        using SpreadsheetDocument document = SpreadsheetDocument.Open(output, false);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidOperationException("O livro exportado não contém estrutura.");
        Worksheet worksheet = workbookPart.WorksheetParts.Single().Worksheet
            ?? throw new InvalidOperationException("O livro exportado não contém folha.");
        string text = worksheet.InnerText;
        Assert.Contains("quantidade", text, StringComparison.Ordinal);
        Assert.Contains("positive_integer_required", text, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(new string('A', 64), text, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string WriteFile(string name, string content)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private string CreateWorkbook(string name, bool includeSecondSheet)
    {
        string path = Path.Combine(_directory, name);
        using SpreadsheetDocument document = SpreadsheetDocument.Create(
            path,
            SpreadsheetDocumentType.Workbook);
        WorkbookPart workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        WorkbookStylesPart stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = new Stylesheet(
            new Fonts(new Font()) { Count = 1U },
            new Fills(new Fill()) { Count = 1U },
            new Borders(new Border()) { Count = 1U },
            new CellStyleFormats(new CellFormat()) { Count = 1U },
            new CellFormats(
                new CellFormat(),
                new CellFormat { NumberFormatId = 14U, ApplyNumberFormat = true })
            { Count = 2U });
        stylesPart.Stylesheet.Save();

        var sheets = new Sheets();
        workbookPart.Workbook.Append(sheets);
        AddSheet(workbookPart, sheets, "Instruções", 1U, [
            [TextCell("A1", "Leia antes de importar")]
        ]);

        if (includeSecondSheet)
        {
            AddSheet(workbookPart, sheets, "Inventário", 2U, [
                [TextCell("A1", "produto"), TextCell("B1", "validade"), TextCell("C1", "quantidade")],
                [
                    TextCell("A2", "Paracetamol"),
                    new Cell { CellReference = "B2", StyleIndex = 1U, CellValue = new CellValue(new DateTime(2027, 12, 31).ToOADate()) },
                    new Cell { CellReference = "C2", CellFormula = new CellFormula("1+1"), CellValue = new CellValue("2") }
                ]
            ]);
        }

        workbookPart.Workbook.Save();
        return path;
    }

    private static void AddSheet(
        WorkbookPart workbookPart,
        Sheets sheets,
        string name,
        uint sheetId,
        IReadOnlyList<IReadOnlyList<Cell>> rows)
    {
        WorksheetPart part = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        uint rowIndex = 1;
        foreach (IReadOnlyList<Cell> cells in rows)
        {
            sheetData.Append(new Row(cells) { RowIndex = rowIndex++ });
        }

        part.Worksheet = new Worksheet(sheetData);
        part.Worksheet.Save();
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(part),
            SheetId = sheetId,
            Name = name
        });
    }

    private static Cell TextCell(string reference, string value) => new()
    {
        CellReference = reference,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(value))
    };
}
