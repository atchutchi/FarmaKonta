using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Nofarma.IntegrationTests.Licensing;

public sealed class PublishedFiles : IDisposable
{
    private const int MaximumTextBytes = 8 * 1024 * 1024;
    private const string EmbeddedPublicKeyResource =
        "Nofarma.Desktop.LicensingPublicKey";
    private static readonly string[] TextExtensions =
    [
        ".b64",
        ".config",
        ".json",
        ".md",
        ".txt",
        ".xml"
    ];

    private readonly SafeTemporaryDirectory _temporaryDirectory;
    private readonly byte[] _allTextBytes;

    public PublishedFiles()
    {
        RepoRoot = FindRepoRoot();
        QaPublicKeyPath = Path.Combine(
            RepoRoot,
            "build",
            "keys",
            "nofarma-qa-public.spki.b64");
        CommercialPublicKeyPath = Path.Combine(
            RepoRoot,
            "build",
            "keys",
            "nofarma-commercial-public.spki.b64");
        _temporaryDirectory = new SafeTemporaryDirectory(
            "nofarma-published-channel");
        string buildOutput = Path.Combine(_temporaryDirectory.Path, "build");
        PublishedDirectory = Path.Combine(_temporaryDirectory.Path, "publish");

        ProcessResult build = RunDotnet(
            RepoRoot,
            "build",
            DesktopProject(RepoRoot),
            "--configuration",
            "Release",
            "--nologo",
            "--disable-build-servers",
            "-warnaserror",
            "-p:NofarmaLicenseChannel=QA",
            "--output",
            buildOutput);
        EnsureSucceeded("QA build", build);
        QaBuildAssemblyPath = FindDesktopAssembly(buildOutput);

        ProcessResult publish = RunDotnet(
            RepoRoot,
            "publish",
            DesktopProject(RepoRoot),
            "--configuration",
            "Release",
            "--nologo",
            "--disable-build-servers",
            "-warnaserror",
            "-p:NofarmaLicenseChannel=QA",
            "--output",
            PublishedDirectory);
        EnsureSucceeded("QA publish", publish);
        QaPublishedAssemblyPath = FindDesktopAssembly(PublishedDirectory);

        Names = Directory
            .EnumerateFiles(PublishedDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(PublishedDirectory, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _allTextBytes = ReadTextFiles(PublishedDirectory, Names);
    }

    public string RepoRoot { get; }

    public string QaPublicKeyPath { get; }

    public string CommercialPublicKeyPath { get; }

    public string PublishedDirectory { get; }

    public string QaBuildAssemblyPath { get; }

    public string QaPublishedAssemblyPath { get; }

    public IReadOnlyList<string> Names { get; }

    public byte[] ReadAllTextBytes() => _allTextBytes.ToArray();

    public bool ContainsAscii(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        byte[] pattern = Encoding.ASCII.GetBytes(value);
        return Directory
            .EnumerateFiles(PublishedDirectory, "*", SearchOption.AllDirectories)
            .Any(path => FileContains(path, pattern));
    }

    public static bool IsForbiddenPublishedFile(string relativePath)
    {
        string fileName = Path.GetFileName(relativePath);
        string extension = Path.GetExtension(relativePath);
        return fileName.Equals("qa-signing-key.bin", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(
                "Nofarma.Licensing.Qa",
                StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".p8", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pem", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".key", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".nofarma-license", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".nofarma-request", StringComparison.OrdinalIgnoreCase);
    }

    public static byte[] ReadPublicKey(string path)
    {
        string base64 = File.ReadAllText(path).Trim();
        return Convert.FromBase64String(base64);
    }

    public static byte[] ReadEmbeddedPublicKey(string assemblyPath)
    {
        Assembly assembly = Assembly.Load(File.ReadAllBytes(assemblyPath));
        using Stream stream = assembly.GetManifestResourceStream(
                EmbeddedPublicKeyResource)
            ?? throw new InvalidOperationException(
                $"The assembly does not contain {EmbeddedPublicKeyResource}.");
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false);
        return Convert.FromBase64String(reader.ReadToEnd().Trim());
    }

    public static bool AreSamePublicKey(
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second)
    {
        ECParameters firstParameters = ReadP256Parameters(first);
        ECParameters secondParameters = ReadP256Parameters(second);
        return CryptographicOperations.FixedTimeEquals(
                firstParameters.Q.X ?? [],
                secondParameters.Q.X ?? [])
            && CryptographicOperations.FixedTimeEquals(
                firstParameters.Q.Y ?? [],
                secondParameters.Q.Y ?? []);
    }

    public static ProcessResult BuildCommercialWithoutKey()
    {
        using var temporaryRepo = CreateTemporaryRepo(
            "nofarma-commercial-without-key");
        CopyQaPublicKey(FindRepoRoot(), temporaryRepo.Path);
        return BuildCommercial(temporaryRepo.Path);
    }

    public static ProcessResult BuildCommercialWithQaKey()
    {
        using var temporaryRepo = CreateTemporaryRepo(
            "nofarma-commercial-qa-key");
        string qaPath = CopyQaPublicKey(FindRepoRoot(), temporaryRepo.Path);
        string commercialPath = CommercialKeyPath(temporaryRepo.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(commercialPath)!);
        File.Copy(qaPath, commercialPath);
        return BuildCommercial(temporaryRepo.Path);
    }

    public static CommercialBuildResult BuildCommercialWithDistinctTemporaryKey()
    {
        SafeTemporaryDirectory temporaryRepo = CreateTemporaryRepo(
            "nofarma-commercial-distinct-key");
        try
        {
            string qaPath = CopyQaPublicKey(FindRepoRoot(), temporaryRepo.Path);
            string commercialPath = CommercialKeyPath(temporaryRepo.Path);
            using ECDsa commercialKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            byte[] commercialPublicKey = commercialKey.ExportSubjectPublicKeyInfo();
            Directory.CreateDirectory(Path.GetDirectoryName(commercialPath)!);
            File.WriteAllText(
                commercialPath,
                Convert.ToBase64String(commercialPublicKey));

            ProcessResult process = BuildCommercial(temporaryRepo.Path);
            string snapshotDirectory = Path.Combine(
                temporaryRepo.Path,
                "src",
                "Nofarma.Desktop",
                "obj",
                "nofarma-license-gate");
            IReadOnlyList<string> snapshots = Directory.Exists(snapshotDirectory)
                ? Directory.GetFiles(
                    snapshotDirectory,
                    "*.validated.spki.b64",
                    SearchOption.TopDirectoryOnly)
                : [];
            string assemblyPath = process.ExitCode == 0
                ? FindDesktopAssembly(Path.Combine(
                    temporaryRepo.Path,
                    "src",
                    "Nofarma.Desktop",
                    "bin"))
                : Path.Combine(temporaryRepo.Path, "missing-Nofarma.Desktop.dll");
            return new CommercialBuildResult(
                temporaryRepo,
                process,
                qaPath,
                commercialPublicKey,
                assemblyPath,
                snapshots);
        }
        catch
        {
            temporaryRepo.Dispose();
            throw;
        }
    }

    public static ProcessResult RunVerificationScript(string channel, string output)
    {
        string repoRoot = FindRepoRoot();
        return RunProcess(
            repoRoot,
            "powershell.exe",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            Path.Combine(repoRoot, "scripts", "verify-license-channel.ps1"),
            "-Channel",
            channel,
            "-Output",
            output);
    }

    public static SafeTemporaryOutput CreateNonEmptyOutputDirectory() => new();

    public void Dispose() => _temporaryDirectory.Dispose();

    private static SafeTemporaryDirectory CreateTemporaryRepo(string prefix)
    {
        var temporaryRepo = new SafeTemporaryDirectory(prefix);
        try
        {
            CopyBuildTree(FindRepoRoot(), temporaryRepo.Path);
            return temporaryRepo;
        }
        catch
        {
            temporaryRepo.Dispose();
            throw;
        }
    }

    private static string CopyQaPublicKey(string sourceRoot, string targetRoot)
    {
        string target = Path.Combine(
            targetRoot,
            "build",
            "keys",
            "nofarma-qa-public.spki.b64");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(
            Path.Combine(
                sourceRoot,
                "build",
                "keys",
                "nofarma-qa-public.spki.b64"),
            target);
        return target;
    }

    private static string CommercialKeyPath(string repoRoot) => Path.Combine(
        repoRoot,
        "build",
        "keys",
        "nofarma-commercial-public.spki.b64");

    private static ProcessResult BuildCommercial(string repoRoot) => RunDotnet(
        repoRoot,
        "build",
        DesktopProject(repoRoot),
        "--configuration",
        "Release",
        "--nologo",
        "--disable-build-servers",
        "-warnaserror",
        "-p:NofarmaLicenseChannel=Commercial");

    private static string DesktopProject(string repoRoot) => Path.Combine(
        repoRoot,
        "src",
        "Nofarma.Desktop",
        "Nofarma.Desktop.csproj");

    private static void CopyBuildTree(string sourceRoot, string targetRoot)
    {
        foreach (string rootFile in new[]
                 {
                     ".editorconfig",
                     "Directory.Build.props",
                     "Directory.Packages.props",
                     "global.json"
                 })
        {
            File.Copy(
                Path.Combine(sourceRoot, rootFile),
                Path.Combine(targetRoot, rootFile));
        }

        foreach (string relativeDirectory in new[]
                 {
                     Path.Combine("src", "Nofarma.Application"),
                     Path.Combine("src", "Nofarma.Contracts"),
                     Path.Combine("src", "Nofarma.Desktop"),
                     Path.Combine("src", "Nofarma.Domain"),
                     Path.Combine("src", "Nofarma.Infrastructure"),
                     Path.Combine("tools", "Nofarma.Licensing.Qa")
                 })
        {
            CopySourceDirectory(
                Path.Combine(sourceRoot, relativeDirectory),
                Path.Combine(targetRoot, relativeDirectory));
        }
    }

    private static void CopySourceDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (string child in Directory.EnumerateDirectories(source))
        {
            string name = Path.GetFileName(child);
            if (name is "bin" or "obj")
            {
                continue;
            }

            CopySourceDirectory(child, Path.Combine(target, name));
        }
    }

    private static byte[] ReadTextFiles(
        string root,
        IEnumerable<string> relativeNames)
    {
        using var output = new MemoryStream();
        foreach (string relativeName in relativeNames)
        {
            if (!IsTextFile(relativeName))
            {
                continue;
            }

            string path = Path.Combine(root, relativeName);
            long remaining = MaximumTextBytes - output.Length;
            if (remaining <= 0 || new FileInfo(path).Length > remaining)
            {
                continue;
            }

            byte[] contents = File.ReadAllBytes(path);
            output.Write(contents);
            output.WriteByte((byte)'\n');
        }

        return output.ToArray();
    }

    private static bool IsTextFile(string path) => TextExtensions.Contains(
        Path.GetExtension(path),
        StringComparer.OrdinalIgnoreCase);

    private static bool FileContains(string path, ReadOnlySpan<byte> pattern)
    {
        using FileStream stream = File.OpenRead(path);
        byte[] buffer = new byte[64 * 1024 + pattern.Length];
        int carry = 0;
        while (true)
        {
            int read = stream.Read(buffer, carry, buffer.Length - carry);
            int available = carry + read;
            if (buffer.AsSpan(0, available).IndexOf(pattern) >= 0)
            {
                return true;
            }

            if (read == 0)
            {
                return false;
            }

            carry = Math.Min(pattern.Length - 1, available);
            buffer.AsSpan(available - carry, carry).CopyTo(buffer);
        }
    }

    private static ECParameters ReadP256Parameters(ReadOnlySpan<byte> publicKey)
    {
        using ECDsa key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(publicKey, out int bytesRead);
        ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
        if (bytesRead != publicKey.Length
            || key.KeySize != 256
            || !string.Equals(
                parameters.Curve.Oid.Value,
                ECCurve.NamedCurves.nistP256.Oid.Value,
                StringComparison.Ordinal))
        {
            throw new CryptographicException("The public key is not an ECDSA P-256 SPKI.");
        }

        return parameters;
    }

    private static string FindDesktopAssembly(string root) => Directory
        .EnumerateFiles(root, "Nofarma.Desktop.dll", SearchOption.AllDirectories)
        .Order(StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault(path => !path.Contains(
            $"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase))
        ?? throw new FileNotFoundException(
            "The Nofarma Desktop assembly was not produced.");

    private static void EnsureSucceeded(string operation, ProcessResult result)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{operation} failed.{Environment.NewLine}{Tail(result.Output, 8000)}");
        }
    }

    private static ProcessResult RunDotnet(
        string workingDirectory,
        params string[] arguments) => RunProcess(
        workingDirectory,
        "dotnet",
        arguments);

    private static ProcessResult RunProcess(
        string workingDirectory,
        string fileName,
        params string[] arguments) => RunProcess(
        workingDirectory,
        fileName,
        TimeSpan.FromMinutes(5),
        arguments);

    private static ProcessResult RunProcess(
        string workingDirectory,
        string fileName,
        TimeSpan timeout,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(checked((int)timeout.TotalMilliseconds)))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            _ = Task.WaitAll(
                [standardOutput, standardError],
                TimeSpan.FromSeconds(10));
            return new ProcessResult(
                -1,
                $"{fileName} exceeded the {timeout.TotalSeconds:0}-second timeout.");
        }

        if (!Task.WaitAll(
                [standardOutput, standardError],
                TimeSpan.FromSeconds(15)))
        {
            return new ProcessResult(
                -2,
                $"{fileName} output streams did not close after process exit.");
        }

        return new ProcessResult(
            process.ExitCode,
            string.Concat(
                standardOutput.Result,
                Environment.NewLine,
                standardError.Result));
    }

    private static string Tail(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters
            ? value
            : value[^maximumCharacters..];

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Nofarma.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root.");
    }
}

public sealed record ProcessResult(int ExitCode, string Output);

public sealed class CommercialBuildResult : IDisposable
{
    private readonly SafeTemporaryDirectory _temporaryRepo;

    internal CommercialBuildResult(
        SafeTemporaryDirectory temporaryRepo,
        ProcessResult process,
        string qaPublicKeyPath,
        byte[] commercialPublicKey,
        string assemblyPath,
        IReadOnlyList<string> validatedSnapshots)
    {
        _temporaryRepo = temporaryRepo;
        Process = process;
        QaPublicKeyPath = qaPublicKeyPath;
        CommercialPublicKey = commercialPublicKey;
        AssemblyPath = assemblyPath;
        ValidatedSnapshots = validatedSnapshots;
    }

    public ProcessResult Process { get; }

    public string QaPublicKeyPath { get; }

    public byte[] CommercialPublicKey { get; }

    public string AssemblyPath { get; }

    public IReadOnlyList<string> ValidatedSnapshots { get; }

    public void Dispose() => _temporaryRepo.Dispose();
}

public sealed class SafeTemporaryOutput : IDisposable
{
    private readonly SafeTemporaryDirectory _directory = new(
        "nofarma-existing-output");

    public SafeTemporaryOutput()
    {
        SentinelPath = System.IO.Path.Combine(_directory.Path, "sentinel.txt");
        File.WriteAllText(SentinelPath, "preserve-this-output");
    }

    public string Path => _directory.Path;

    public string SentinelPath { get; }

    public void Dispose() => _directory.Dispose();
}

internal sealed class SafeTemporaryDirectory : IDisposable
{
    private readonly string _prefix;

    public SafeTemporaryDirectory(string prefix)
    {
        _prefix = prefix;
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        string fullDirectory = System.IO.Path.GetFullPath(Path);
        string fullTemp = System.IO.Path.GetFullPath(
                System.IO.Path.GetTempPath())
            .TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar)
            + System.IO.Path.DirectorySeparatorChar;
        if (!fullDirectory.StartsWith(
                fullTemp,
                StringComparison.OrdinalIgnoreCase)
            || !System.IO.Path.GetFileName(fullDirectory).StartsWith(
                _prefix,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The published-channel test directory is not safe to remove.");
        }

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                if (Directory.Exists(fullDirectory))
                {
                    Directory.Delete(fullDirectory, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 19)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(50));
            }
            catch (UnauthorizedAccessException) when (attempt < 19)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(50));
            }
        }
    }
}
