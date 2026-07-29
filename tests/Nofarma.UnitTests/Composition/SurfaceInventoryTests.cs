using Nofarma.Infrastructure.Import;

namespace Nofarma.UnitTests.Composition;

public sealed class SurfaceInventoryTests
{
    [Theory]
    [InlineData("ProductsPage.xaml", "Produtos")]
    [InlineData("SuppliersPage.xaml", "Fornecedores")]
    [InlineData("StockPage.xaml", "Stock")]
    [InlineData("PurchasesPage.xaml", "Compras")]
    [InlineData("InventoryImportPage.xaml", "Importar inventário")]
    public void InventorySurfacesExistWithRealEmptyStates(string fileName, string title)
    {
        string root = FindRepositoryRoot();
        string path = Path.Combine(root, "src", "Nofarma.Desktop", "Views", fileName);

        Assert.True(File.Exists(path), $"Falta a superfície {fileName}.");
        string xaml = File.ReadAllText(path);
        Assert.Contains(title, xaml, StringComparison.Ordinal);
        Assert.Contains("Ainda não", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Paracetamol", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("156", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellRoutesInventoryDestinationsToFunctionalPages()
    {
        string root = FindRepositoryRoot();
        string code = File.ReadAllText(Path.Combine(
            root, "src", "Nofarma.Desktop", "Views", "AppShellPage.xaml.cs"));

        Assert.Contains("new ProductsPage()", code, StringComparison.Ordinal);
        Assert.Contains("new SuppliersPage()", code, StringComparison.Ordinal);
        Assert.Contains("new StockPage()", code, StringComparison.Ordinal);
        Assert.Contains("new PurchasesPage()", code, StringComparison.Ordinal);
        Assert.Contains("new InventoryImportPage()", code, StringComparison.Ordinal);
        Assert.Contains("LowStockProducts", code, StringComparison.Ordinal);
        Assert.Contains("OutOfStockProducts", code, StringComparison.Ordinal);
        Assert.Contains("ExpiryAttentionLots", code, StringComparison.Ordinal);
        Assert.DoesNotContain("StockAlertText.Visibility = Visibility.Visible", code, StringComparison.Ordinal);

        string shell = File.ReadAllText(Path.Combine(
            root, "src", "Nofarma.Desktop", "Views", "AppShellPage.xaml"));
        Assert.Contains("MinWindowWidth=\"1450\"", shell, StringComparison.Ordinal);
        Assert.Contains("Target=\"StockAlertText.Visibility\" Value=\"Collapsed\"", shell, StringComparison.Ordinal);
        Assert.Contains("Target=\"StockAlertText.Visibility\" Value=\"Visible\"", shell, StringComparison.Ordinal);
        Assert.Contains("Target=\"LocalDataText.Visibility\" Value=\"Collapsed\"", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("Target=\"ActivationText.Visibility\" Value=\"Collapsed\"", shell, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ActivationText\"", shell, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"*\" />", shell, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"Auto\" />", shell, StringComparison.Ordinal);
        Assert.Contains("TextTrimming=\"CharacterEllipsis\"", shell, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfficialImportTemplateContainsHeadersAndNoDataRows()
    {
        string root = FindRepositoryRoot();
        string path = Path.Combine(root, "docs", "development", "inventory-import-template.csv");

        var file = await CsvInventoryFileReader.ReadAsync(path, TestContext.Current.CancellationToken);

        Assert.Contains("Nome comercial", file.Headers);
        Assert.Contains("Unidade base", file.Headers);
        Assert.Contains("Quantidade inicial", file.Headers);
        Assert.Empty(file.Rows);
    }

    [Fact]
    public void DesktopStartsAtApprovedInventoryViewport()
    {
        string root = FindRepositoryRoot();
        string mainWindow = File.ReadAllText(Path.Combine(
            root, "src", "Nofarma.Desktop", "MainWindow.xaml.cs"));

        Assert.Contains("SizeInt32(1366, 768)", mainWindow, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Nofarma.Desktop")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Não foi possível localizar a raiz do repositório.");
    }
}
