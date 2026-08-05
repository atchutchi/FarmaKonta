using System.Xml.Linq;

namespace Nofarma.UnitTests.Desktop;

public sealed class LoginPageMarkupTests
{
    [Fact]
    public void LoginPageExposesTheOfflineRecoveryAction()
    {
        string repositoryRoot = FindRepositoryRoot();
        XDocument document = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "Nofarma.Desktop",
            "Views",
            "LoginPage.xaml"));
        XElement recoveryButton = Assert.Single(
            document.Descendants(),
            element =>
                element.Name.LocalName == "Button" &&
                (string?)element.Attribute("Content") == "Recuperar acesso");

        Assert.Equal("OnRecoverAccess", (string?)recoveryButton.Attribute("Click"));
        Assert.Equal("44", (string?)recoveryButton.Attribute("MinHeight"));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Nofarma.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "Não foi possível localizar a raiz do repositório.");
    }
}
