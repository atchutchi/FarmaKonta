[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Unlicensed", "QA", "Commercial")]
    [string]$Channel,

    [Parameter(Mandatory = $true)]
    [string]$Output
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$env:MSBUILDDISABLENODEREUSE = "1"
$env:DOTNET_CLI_USE_MSBUILD_SERVER = "0"

function Test-SamePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$First,

        [Parameter(Mandatory = $true)]
        [string]$Second
    )

    $normalizedFirst = $First.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $normalizedSecond = $Second.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    return [string]::Equals(
        $normalizedFirst,
        $normalizedSecond,
        [StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoReparsePoint {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $candidate = $Path
    $foundExistingParent = $false
    while (-not [string]::IsNullOrWhiteSpace($candidate)) {
        if (Test-Path -LiteralPath $candidate) {
            $foundExistingParent = $true
            $item = Get-Item -LiteralPath $candidate -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "The output path cannot use a reparse point."
            }
        }

        $parent = [IO.Path]::GetDirectoryName($candidate)
        if ([string]::Equals(
                $parent,
                $candidate,
                [StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $candidate = $parent
    }

    if (-not $foundExistingParent) {
        throw "The output path has no existing parent directory."
    }
}

function Invoke-Dotnet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$CommandArguments
    )

    & dotnet @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE."
    }
}

function Get-DesktopAssembly {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $assemblies = @(
        Get-ChildItem -LiteralPath $Root -Filter "Nofarma.Desktop.dll" -File -Recurse |
            Where-Object {
                $_.FullName -notmatch "[\\/]ref[\\/]"
            }
    )
    if ($assemblies.Count -ne 1) {
        throw "Expected exactly one Nofarma.Desktop.dll under the channel output."
    }

    return $assemblies[0].FullName
}

function Assert-EmbeddedChannelKey {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AssemblyPath,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedPublicKeyPath,

        [string]$OppositePublicKeyPath
    )

    $arguments = @(
        "run",
        "--project",
        $script:IssuerProject,
        "--configuration",
        "Release",
        "--no-launch-profile",
        "--",
        "validate-published-key",
        "--assembly",
        $AssemblyPath,
        "--expected-public",
        $ExpectedPublicKeyPath
    )
    if (-not [string]::IsNullOrWhiteSpace($OppositePublicKeyPath)) {
        $arguments += @("--opposite-public", $OppositePublicKeyPath)
    }

    Invoke-Dotnet -CommandArguments $arguments
}

function Assert-NoEmbeddedChannelKey {
    param(
        [Parameter(Mandatory = $true)]
        [string]$AssemblyPath
    )

    Invoke-Dotnet -CommandArguments @(
        "run",
        "--project",
        $script:IssuerProject,
        "--configuration",
        "Release",
        "--no-launch-profile",
        "--",
        "validate-published-key",
        "--assembly",
        $AssemblyPath,
        "--expect-absent"
    )
}

function Assert-PublishedFilesSafe {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,

        [string]$OppositePublicKeyPath
    )

    $arguments = @(
        "run",
        "--project",
        $script:IssuerProject,
        "--configuration",
        "Release",
        "--no-launch-profile",
        "--no-build",
        "--",
        "scan-published-output",
        "--root",
        $Root
    )
    if (-not [string]::IsNullOrWhiteSpace($OppositePublicKeyPath)) {
        $arguments += @("--opposite-public", $OppositePublicKeyPath)
    }

    Invoke-Dotnet -CommandArguments $arguments
}

$isDriveAbsolute = $Output -match "^[A-Za-z]:[\\/]"
$isUncAbsolute = $Output.StartsWith("\\", [StringComparison]::Ordinal)
if ((-not $isDriveAbsolute -and -not $isUncAbsolute) -or
    $Output.StartsWith("\\?\", [StringComparison]::Ordinal) -or
    $Output.StartsWith("\\.\", [StringComparison]::Ordinal)) {
    throw "The output path must be absolute."
}

$repoRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$fullOutput = [IO.Path]::GetFullPath($Output)
$outputRoot = [IO.Path]::GetPathRoot($fullOutput)
if (Test-SamePath -First $fullOutput -Second $outputRoot) {
    throw "The output path must be a dedicated directory, not a drive root."
}

if (Test-SamePath -First $fullOutput -Second $repoRoot) {
    throw "The repository root cannot be used as the publish output."
}

Assert-NoReparsePoint -Path $fullOutput
if (Test-Path -LiteralPath $fullOutput) {
    $outputItem = Get-Item -LiteralPath $fullOutput -Force
    if (-not $outputItem.PSIsContainer) {
        throw "The output path must be a directory."
    }

    if (@(Get-ChildItem -LiteralPath $fullOutput -Force).Count -ne 0) {
        throw "The output directory must be empty. Existing contents were preserved."
    }
}
else {
    [IO.Directory]::CreateDirectory($fullOutput) | Out-Null
}

$desktopProject = Join-Path $repoRoot "src\Nofarma.Desktop\Nofarma.Desktop.csproj"
$script:IssuerProject = Join-Path $repoRoot "tools\Nofarma.Licensing.Qa\Nofarma.Licensing.Qa.csproj"
$qaPublicKey = Join-Path $repoRoot "build\keys\nofarma-qa-public.spki.b64"
$commercialPublicKey = Join-Path $repoRoot "build\keys\nofarma-commercial-public.spki.b64"
$expectedPublicKey = $null
$oppositePublicKey = $null
switch ($Channel) {
    "QA" {
        if (-not (Test-Path -LiteralPath $qaPublicKey -PathType Leaf)) {
            throw "The fixed QA public key is not provisioned."
        }

        $expectedPublicKey = $qaPublicKey
        if (Test-Path -LiteralPath $commercialPublicKey -PathType Leaf) {
            $oppositePublicKey = $commercialPublicKey
        }
    }
    "Commercial" {
        if (-not (Test-Path -LiteralPath $commercialPublicKey -PathType Leaf)) {
            throw "NFLC001: The Commercial public key is not provisioned."
        }

        if (-not (Test-Path -LiteralPath $qaPublicKey -PathType Leaf)) {
            throw "NFLC003: The QA public key is not provisioned."
        }

        $expectedPublicKey = $commercialPublicKey
        $oppositePublicKey = $qaPublicKey
    }
}

$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$privateBuildRoot = [IO.Path]::Combine(
    $temporaryRoot,
    "nofarma-license-verify-$([Guid]::NewGuid().ToString('N'))")
$buildOutput = [IO.Path]::Combine($privateBuildRoot, "build")
if (Test-Path -LiteralPath $privateBuildRoot) {
    throw "The private build output unexpectedly already exists."
}

[IO.Directory]::CreateDirectory($buildOutput) | Out-Null
try {
    Invoke-Dotnet -CommandArguments @(
        "build",
        $desktopProject,
        "--configuration",
        "Release",
        "--nologo",
        "--disable-build-servers",
        "-warnaserror",
        "-p:NofarmaLicenseChannel=$Channel",
        "--output",
        $buildOutput
    )
    $builtAssembly = Get-DesktopAssembly -Root $buildOutput
    if ($Channel -eq "Unlicensed") {
        Assert-NoEmbeddedChannelKey -AssemblyPath $builtAssembly
    }
    else {
        Assert-EmbeddedChannelKey `
            -AssemblyPath $builtAssembly `
            -ExpectedPublicKeyPath $expectedPublicKey `
            -OppositePublicKeyPath $oppositePublicKey
    }

    Invoke-Dotnet -CommandArguments @(
        "publish",
        $desktopProject,
        "--configuration",
        "Release",
        "--nologo",
        "--disable-build-servers",
        "-warnaserror",
        "-p:NofarmaLicenseChannel=$Channel",
        "--output",
        $fullOutput
    )
    $publishedAssembly = Get-DesktopAssembly -Root $fullOutput
    if ($Channel -eq "Unlicensed") {
        Assert-NoEmbeddedChannelKey -AssemblyPath $publishedAssembly
    }
    else {
        Assert-EmbeddedChannelKey `
            -AssemblyPath $publishedAssembly `
            -ExpectedPublicKeyPath $expectedPublicKey `
            -OppositePublicKeyPath $oppositePublicKey
    }

    Assert-PublishedFilesSafe `
        -Root $fullOutput `
        -OppositePublicKeyPath $oppositePublicKey
}
finally {
    $resolvedBuildOutput = [IO.Path]::GetFullPath($privateBuildRoot)
    $requiredPrefix = $temporaryRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($resolvedBuildOutput.StartsWith(
            $requiredPrefix,
            [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedBuildOutput).StartsWith(
            "nofarma-license-verify-",
            [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedBuildOutput -Recurse -Force
    }
    else {
        throw "The private build output failed its cleanup path guard."
    }
}

Write-Host "Verified $Channel build and publish at $fullOutput."
