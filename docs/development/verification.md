# Verificação da fundação NôFarma

Executar a partir da raiz do repositório:

```powershell
dotnet restore Nofarma.slnx --locked-mode
dotnet format Nofarma.slnx --verify-no-changes --no-restore
dotnet build Nofarma.slnx --configuration Release --no-restore -warnaserror
dotnet test Nofarma.slnx --configuration Release --no-build
git diff --check
git status --short
```

Para validar a configuração Docker sem revelar credenciais:

```powershell
$env:NOFARMA_POSTGRES_PASSWORD = [Guid]::NewGuid().ToString("N")
docker compose -f deploy/compose.dev.yml config --quiet
Remove-Item Env:NOFARMA_POSTGRES_PASSWORD
```

O gate passa quando todos os comandos terminam com código zero, o build não contém avisos, todos os testes passam e `git status --short` não apresenta ficheiros gerados.

## Identidade local

Executar adicionalmente:

```powershell
dotnet test tests/Nofarma.UnitTests/Nofarma.UnitTests.csproj --filter "FullyQualifiedName~Identity" --no-restore
dotnet test tests/Nofarma.IntegrationTests/Nofarma.IntegrationTests.csproj --filter "FullyQualifiedName~LocalIdentity" --no-restore
dotnet list Nofarma.slnx package --vulnerable --include-transitive
```

O resultado esperado é zero testes falhados e nenhuma dependência vulnerável conhecida. O teste SQLite confirma uma versão igual ou superior a 3.50.2.

## Verificação visual Windows

Compilar e iniciar a aplicação sem MSIX:

```powershell
dotnet build src/Nofarma.Desktop/Nofarma.Desktop.csproj --configuration Debug --no-restore -warnaserror
& "src/Nofarma.Desktop/bin/Debug/net10.0-windows10.0.19041.0/win-x64/Nofarma.Desktop.exe"
```

Numa base vazia, confirmar o assistente de quatro passos. Depois da configuração, confirmar o acesso por palavra-passe e PIN, o shell, a criação de Caixa e a recusa de acesso do Caixa a Utilizadores. O atlas em `docs/design/previews` é a referência visual. Os estados vazios não devem apresentar números ou operações simuladas.
