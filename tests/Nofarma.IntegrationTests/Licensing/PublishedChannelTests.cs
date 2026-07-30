using System.Security.Cryptography;
using System.Text;
using Nofarma.Licensing.Qa;

namespace Nofarma.IntegrationTests.Licensing;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PublishedChannelTestGroup
{
    public const string Name = "Published licensing channel";
}

[Collection(PublishedChannelTestGroup.Name)]
public sealed class PublishedChannelTests(PublishedFiles publishedFiles)
    : IClassFixture<PublishedFiles>
{
    [Fact]
    public void QaBuildAndPublishEmbedTheFixedQaKeySemantically()
    {
        byte[] expected = PublishedFiles.ReadPublicKey(
            publishedFiles.QaPublicKeyPath);
        byte[] built = PublishedFiles.ReadEmbeddedPublicKey(
            publishedFiles.QaBuildAssemblyPath);
        byte[] published = PublishedFiles.ReadEmbeddedPublicKey(
            publishedFiles.QaPublishedAssemblyPath);

        Assert.True(PublishedFiles.AreSamePublicKey(expected, built));
        Assert.True(PublishedFiles.AreSamePublicKey(expected, published));
    }

    [Fact]
    public void QaPublishContainsNoIssuerOrPrivateMaterial()
    {
        Assert.DoesNotContain(
            publishedFiles.Names,
            name => name.Contains(
                "Nofarma.Licensing.Qa",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            publishedFiles.Names,
            name => PublishedFiles.IsForbiddenPublishedFile(name));
        Assert.DoesNotContain(
            "PRIVATE KEY",
            Encoding.UTF8.GetString(publishedFiles.ReadAllTextBytes()),
            StringComparison.OrdinalIgnoreCase);
        Assert.False(publishedFiles.ContainsAscii("Nofarma.Licensing.Qa"));
        Assert.False(publishedFiles.ContainsAscii("BEGIN EC PRIVATE KEY"));
        Assert.False(publishedFiles.ContainsAscii("BEGIN PRIVATE KEY"));
    }

    [Fact]
    public void QaPublishDoesNotContainCommercialChannelMaterial()
    {
        Assert.DoesNotContain(
            publishedFiles.Names,
            name => name.Contains(
                "nofarma-commercial-public",
                StringComparison.OrdinalIgnoreCase));

        if (File.Exists(publishedFiles.CommercialPublicKeyPath))
        {
            byte[] commercial = PublishedFiles.ReadPublicKey(
                publishedFiles.CommercialPublicKeyPath);
            byte[] embedded = PublishedFiles.ReadEmbeddedPublicKey(
                publishedFiles.QaPublishedAssemblyPath);
            Assert.False(PublishedFiles.AreSamePublicKey(commercial, embedded));
        }
    }

    [Fact]
    public void CommercialBuildFailsClosedWithoutProvisionedPublicKey()
    {
        ProcessResult result = PublishedFiles.BuildCommercialWithoutKey();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("NFLC001", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CommercialBuildRejectsTheQaPublicKey()
    {
        ProcessResult result = PublishedFiles.BuildCommercialWithQaKey();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("NFLC002", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void CommercialBuildRemovesItsValidatedSnapshotAfterEmbedding()
    {
        using CommercialBuildResult result =
            PublishedFiles.BuildCommercialWithDistinctTemporaryKey();

        Assert.True(result.Process.ExitCode == 0, result.Process.Output);
        Assert.Empty(result.ValidatedSnapshots);
        Assert.True(PublishedFiles.AreSamePublicKey(
            result.CommercialPublicKey,
            PublishedFiles.ReadEmbeddedPublicKey(result.AssemblyPath)));
        Assert.False(PublishedFiles.AreSamePublicKey(
            PublishedFiles.ReadPublicKey(result.QaPublicKeyPath),
            PublishedFiles.ReadEmbeddedPublicKey(result.AssemblyPath)));
    }
}

[Collection(PublishedChannelTestGroup.Name)]
public sealed class LicenseChannelScriptTests
{
    [Fact]
    public void VerificationScriptRejectsRelativeOutput()
    {
        ProcessResult result = PublishedFiles.RunVerificationScript(
            "QA",
            Path.Combine("artifacts", "relative-license-output"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("absolute", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerificationScriptPreservesExistingNonEmptyOutput()
    {
        using var output = PublishedFiles.CreateNonEmptyOutputDirectory();
        byte[] expected = File.ReadAllBytes(output.SentinelPath);

        ProcessResult result = PublishedFiles.RunVerificationScript(
            "QA",
            output.Path);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(File.Exists(output.SentinelPath));
        Assert.Equal(expected, File.ReadAllBytes(output.SentinelPath));
    }

    [Fact]
    public void PublishedOutputScannerRejectsSecretAcrossBufferBoundary()
    {
        using var output = new SafeTemporaryDirectory(
            "nofarma-binary-boundary");
        byte[] contents = new byte[(1024 * 1024) + 32];
        byte[] marker = Encoding.ASCII.GetBytes("PRIVATE KEY");
        marker.CopyTo(contents, (1024 * 1024) - 4);
        File.WriteAllBytes(Path.Combine(output.Path, "probe.bin"), contents);

        LicenseChannelKeyValidation result =
            QaPublishedOutputScanner.Validate(output.Path);

        Assert.False(result.IsValid);
        Assert.Equal("NFLC010", result.Code);
    }

    [Fact]
    public void PublishedOutputScannerMatchesAsciiWithoutCaseSensitivity()
    {
        using var output = new SafeTemporaryDirectory(
            "nofarma-binary-case");
        File.WriteAllBytes(
            Path.Combine(output.Path, "probe.bin"),
            Encoding.ASCII.GetBytes("prefix-private key-suffix"));

        LicenseChannelKeyValidation result =
            QaPublishedOutputScanner.Validate(output.Path);

        Assert.False(result.IsValid);
        Assert.Equal("NFLC010", result.Code);
    }

    [Fact]
    public void PublishedOutputScannerAcceptsLargeBinaryWithoutMarker()
    {
        using var output = new SafeTemporaryDirectory(
            "nofarma-binary-clean");
        File.WriteAllBytes(
            Path.Combine(output.Path, "clean.bin"),
            Enumerable.Repeat((byte)0x5A, 2 * 1024 * 1024).ToArray());

        LicenseChannelKeyValidation result =
            QaPublishedOutputScanner.Validate(output.Path);

        Assert.True(result.IsValid, result.Message);
    }

    [Theory]
    [InlineData("private.p8")]
    [InlineData("qa-signing-key.bin")]
    [InlineData("Nofarma.Licensing.Qa.dll")]
    [InlineData("issued.nofarma-license")]
    public void PublishedOutputScannerRejectsForbiddenFileName(string fileName)
    {
        using var output = new SafeTemporaryDirectory(
            "nofarma-binary-name");
        File.WriteAllBytes(Path.Combine(output.Path, fileName), [1, 2, 3]);

        LicenseChannelKeyValidation result =
            QaPublishedOutputScanner.Validate(output.Path);

        Assert.False(result.IsValid);
        Assert.Equal("NFLC010", result.Code);
    }

    [Fact]
    public void PublishedOutputScannerRejectsOppositeChannelKeyBytes()
    {
        using var directory = new SafeTemporaryDirectory(
            "nofarma-opposite-key");
        string publish = Path.Combine(directory.Path, "publish");
        Directory.CreateDirectory(publish);
        using ECDsa oppositeKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] oppositePublicKey = oppositeKey.ExportSubjectPublicKeyInfo();
        string oppositePath = Path.Combine(directory.Path, "opposite.spki.b64");
        File.WriteAllText(oppositePath, Convert.ToBase64String(oppositePublicKey));
        File.WriteAllBytes(
            Path.Combine(publish, "renamed.bin"),
            [0, .. oppositePublicKey, 255]);

        LicenseChannelKeyValidation result =
            QaPublishedOutputScanner.Validate(publish, oppositePath);

        Assert.False(result.IsValid);
        Assert.Equal("NFLC010", result.Code);
    }
}
