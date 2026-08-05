namespace Nofarma.UnitTests.Composition;

public sealed class SurfaceSalesTests
{
    [Fact]
    public void SalesSurfaceIsKeyboardFirstAccessibleAndUsesRealDataOnly()
    {
        string root = FindRepositoryRoot();
        string path = Path.Combine(root, "src", "Nofarma.Desktop", "Views", "SalesPage.xaml");

        Assert.True(File.Exists(path), "Falta a superfície SalesPage.xaml.");
        string xaml = File.ReadAllText(path);
        Assert.Contains("Text=\"Vendas\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"F2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"F4\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"F6\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"F8\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"Escape\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Pesquisar produto por código de barras, código ou nome\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"Carrinho da venda\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Ainda não existem produtos no carrinho", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompleteSaleButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"CompactSalesLayout\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWindowWidth=\"1366\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PARACETAMOL", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("1 100 XOF", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellRoutesSalesToTheFunctionalPage()
    {
        string root = FindRepositoryRoot();
        string code = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Nofarma.Desktop",
            "Views",
            "AppShellPage.xaml.cs"));

        Assert.Contains("case \"Vendas\":", code, StringComparison.Ordinal);
        Assert.Contains("new SalesPage()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesCodeBehindBindsSubmittingAndAccessibleCartActions()
    {
        string root = FindRepositoryRoot();
        string code = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Nofarma.Desktop",
            "Views",
            "SalesPage.xaml.cs"));

        Assert.Contains("CompleteSaleButton.IsEnabled = !_viewModel.IsSubmitting", code, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName", code, StringComparison.Ordinal);
        Assert.Contains("OnSearchAccelerator", code, StringComparison.Ordinal);
        Assert.Contains("OnSuspendAccelerator", code, StringComparison.Ordinal);
        Assert.Contains("OnSuspendedSalesAccelerator", code, StringComparison.Ordinal);
        Assert.Contains("OnPaymentAccelerator", code, StringComparison.Ordinal);
        Assert.Contains("OnEscapeAccelerator", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ReceiptPreviewIsInternalNonFiscalAndSimulationOnly()
    {
        string root = FindRepositoryRoot();
        string xamlPath = Path.Combine(
            root,
            "src",
            "Nofarma.Desktop",
            "Views",
            "ReceiptPreviewDialog.xaml");
        string codePath = Path.Combine(
            root,
            "src",
            "Nofarma.Desktop",
            "Views",
            "ReceiptPreviewDialog.xaml.cs");

        Assert.True(File.Exists(xamlPath), "Falta a pré-visualização do recibo interno.");
        Assert.True(File.Exists(codePath), "Falta a lógica do simulador de impressão.");
        string xaml = File.ReadAllText(xamlPath);
        string code = File.ReadAllText(codePath);
        Assert.Contains("Recibo interno não fiscal", xaml, StringComparison.Ordinal);
        Assert.Contains("Simular impressão", xaml, StringComparison.Ordinal);
        Assert.Contains("Enviado ao simulador", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Factura oficial", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DGCI autorizada", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("File.", code, StringComparison.Ordinal);
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
