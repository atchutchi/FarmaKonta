namespace Nofarma.UnitTests.Composition;

public sealed class SurfaceInventoryTests
{
    [Theory]
    [InlineData("ProductsPage.xaml", "Produtos")]
    [InlineData("SuppliersPage.xaml", "Fornecedores")]
    [InlineData("StockPage.xaml", "Stock")]
    [InlineData("PurchasesPage.xaml", "Compras")]
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
