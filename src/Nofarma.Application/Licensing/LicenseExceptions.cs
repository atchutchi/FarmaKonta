namespace Nofarma.Application.Licensing;

public sealed class LicenseImportException(string code)
    : InvalidOperationException("A licença não pôde ser importada.")
{
    public string Code { get; } = code;
}

public sealed class LicenseContextUnavailableException()
    : InvalidOperationException("A instalação local não está disponível para licenciamento.");

public sealed class LicenseOperationBlockedException(string code)
    : InvalidOperationException("A operação requer uma licença activa.")
{
    public string Code { get; } = code;
}
