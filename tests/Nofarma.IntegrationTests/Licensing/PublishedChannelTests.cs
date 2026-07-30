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
    public static TheoryData<string, string, bool> PrivateHeaderCases
    {
        get
        {
            string[] headers =
            [
                string.Concat(
                    "-----BEGIN ",
                    "PRIVATE KEY-----"),
                string.Concat(
                    "-----BEGIN ENCRYPTED ",
                    "PRIVATE KEY-----"),
                string.Concat(
                    "-----BEGIN RSA ",
                    "PRIVATE KEY-----"),
                string.Concat(
                    "-----BEGIN EC ",
                    "PRIVATE KEY-----"),
                string.Concat(
                    "-----BEGIN OPENSSH ",
                    "PRIVATE KEY-----"),
                string.Concat(
                    "-----BEGIN DSA ",
                    "PRIVATE KEY-----"),
                string.Concat(
                    "-----BEGIN ED25519 ",
                    "PRIVATE KEY-----"),
                string.Concat(
                    "-----BEGIN PGP ",
                    "PRIVATE KEY BLOCK-----"),
                string.Concat(
                    "---- BEGIN SSH2 ENCRYPTED ",
                    "PRIVATE KEY ----")
            ];
            string[] encodingNames =
            [
                "utf8",
                "utf16le",
                "utf16be",
                "utf32le",
                "utf32be"
            ];
            var cases = new TheoryData<string, string, bool>();
            foreach (string header in headers)
            {
                foreach (string encodingName in encodingNames)
                {
                    cases.Add(header, encodingName, false);
                    cases.Add(header, encodingName, true);
                }
            }

            return cases;
        }
    }

    public static TheoryData<string, bool> EncodedTextCases
    {
        get
        {
            var cases = new TheoryData<string, bool>();
            foreach (string encodingName in new[]
                     {
                         "utf8",
                         "utf16le",
                         "utf16be",
                         "utf32le",
                         "utf32be"
                     })
            {
                cases.Add(encodingName, false);
                cases.Add(encodingName, true);
            }

            return cases;
        }
    }

    public static TheoryData<string, bool, bool> OppositeKeyTextCases
    {
        get
        {
            var cases = new TheoryData<string, bool, bool>();
            foreach (string encodingName in new[]
                     {
                         "utf8",
                         "utf16le",
                         "utf16be",
                         "utf32le",
                         "utf32be"
                     })
            {
                cases.Add(encodingName, false, false);
                cases.Add(encodingName, false, true);
                cases.Add(encodingName, true, false);
                cases.Add(encodingName, true, true);
            }

            return cases;
        }
    }

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
    public void VerificationScriptRejectsOutputChangedBeforePublish()
    {
        using var output = new SafeTemporaryDirectory(
            "nofarma-late-publish-output");
        string sentinelPath = Path.Combine(output.Path, "late-sentinel.txt");

        ProcessResult result = PublishedFiles
            .RunVerificationScriptWithLateOutputFile(
                "QA",
                output.Path,
                sentinelPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("empty", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "preserve-late-output",
            File.ReadAllText(sentinelPath));
    }

    [Fact]
    public void VerificationScriptPublishesUnlicensedWithoutKeyResource()
    {
        using var output = new SafeTemporaryDirectory(
            "nofarma-unlicensed-script-success");

        ProcessResult result = PublishedFiles.RunVerificationScript(
            "Unlicensed",
            output.Path);
        LicenseChannelKeyValidation validation =
            QaPublishedKeyValidator.ValidateAbsent(
                PublishedFiles.FindPublishedDesktopAssembly(output.Path));

        Assert.True(result.ExitCode == 0, result.Output);
        Assert.True(validation.IsValid, validation.Message);
    }

    [Fact]
    public void VerificationScriptPublishesCommercialWithEphemeralKey()
    {
        using CommercialScriptResult result = PublishedFiles
            .RunCommercialVerificationScriptWithDistinctTemporaryKey();

        byte[] embedded = PublishedFiles.ReadEmbeddedPublicKey(
            PublishedFiles.FindPublishedDesktopAssembly(
                result.PublishedDirectory));

        Assert.True(result.Process.ExitCode == 0, result.Process.Output);
        Assert.True(PublishedFiles.AreSamePublicKey(
            result.CommercialPublicKey,
            embedded));
        Assert.False(PublishedFiles.AreSamePublicKey(
            PublishedFiles.ReadPublicKey(result.QaPublicKeyPath),
            embedded));
    }

    [Fact]
    public void PublishedOutputScannerRejectsSecretAcrossBufferBoundary()
    {
        using var output = new SafeTemporaryDirectory(
            "nofarma-binary-boundary");
        byte[] contents = new byte[(64 * 1024) + 32];
        byte[] marker = Encoding.ASCII.GetBytes("PRIVATE KEY");
        marker.CopyTo(contents, (64 * 1024) - 4);
        File.WriteAllBytes(Path.Combine(output.Path, "probe.bin"), contents);

        LicenseChannelKeyValidation result =
            QaPublishedOutputScanner.Validate(output.Path);

        Assert.False(result.IsValid);
        Assert.Equal("NFLC010", result.Code);
    }

    [Theory]
    [MemberData(nameof(PrivateHeaderCases))]
    public void PublishedOutputScannerRejectsPrivateHeaderAcrossBufferBoundary(
        string header,
        string encodingName,
        bool includeBom)
    {
        using var output = new SafeTemporaryDirectory(
            "nofarma-encoded-secret");
        Encoding encoding = MarkerEncoding(encodingName, includeBom);
        byte[] marker = encoding.GetBytes(header);
        byte[] preamble = includeBom ? encoding.GetPreamble() : [];
        int markerStart = (64 * 1024) - Math.Max(1, marker.Length / 2);
        byte[] contents = Enumerable
            .Repeat((byte)0x7F, markerStart + marker.Length + 32)
            .ToArray();
        preamble.CopyTo(contents, 0);
        marker.CopyTo(contents, markerStart);
        File.WriteAllBytes(Path.Combine(output.Path, "encoded.bin"), contents);

        LicenseChannelKeyValidation result =
            QaPublishedOutputScanner.Validate(output.Path);

        Assert.False(result.IsValid);
        Assert.Equal("NFLC010", result.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishedOutputScannerRejectsUtf16PrivateKeyText(bool includeBom)
    {
        using var output = new SafeTemporaryDirectory(
            "nofarma-utf16-private-key-text");
        var encoding = new UnicodeEncoding(
            bigEndian: false,
            byteOrderMark: includeBom);
        byte[] preamble = includeBom ? encoding.GetPreamble() : [];
        byte[] contents =
        [
            .. preamble,
            .. encoding.GetBytes("PRIVATE KEY")
        ];
        File.WriteAllBytes(Path.Combine(output.Path, "secret.txt"), contents);

        LicenseChannelKeyValidation result =
            QaPublishedOutputScanner.Validate(output.Path);

        Assert.False(result.IsValid);
        Assert.Equal("NFLC010", result.Code);
    }

    [Fact]
    public void PublishedOutputScannerAcceptsPrivateKeyPhraseInUtf16Binary()
    {
        using var output = new SafeTemporaryDirectory(
            "nofarma-utf16-private-key-binary");
        byte[] contents = new UnicodeEncoding(
            bigEndian: false,
            byteOrderMark: false)
            .GetBytes("ordinary binary metadata PRIVATE KEY category");
        File.WriteAllBytes(Path.Combine(output.Path, "renamed.bin"), contents);

        LicenseChannelKeyValidation result =
            QaPublishedOutputScanner.Validate(output.Path);

        Assert.True(result.IsValid, result.Message);
    }

    [Fact]
    public void PublishedOutputScannerAcceptsPrivateKeyAfterHeaderLine()
    {
        using var output = new SafeTemporaryDirectory(
            "nofarma-private-key-next-line");
        string contents = string.Concat(
            "-----BEGIN PUBLIC MATERIAL-----",
            Environment.NewLine,
            "PRIVATE KEY");
        File.WriteAllBytes(
            Path.Combine(output.Path, "renamed.bin"),
            new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: false)
                .GetBytes(contents));

        LicenseChannelKeyValidation result =
            QaPublishedOutputScanner.Validate(output.Path);

        Assert.True(result.IsValid, result.Message);
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
    [InlineData("private.p12")]
    [InlineData("private.pfx")]
    [InlineData("private.ppk")]
    [InlineData("private.snk")]
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

    [Fact]
    public void PublishedOutputScannerRejectsUtf16OppositeKeyAcrossBufferBoundary()
    {
        using var directory = new SafeTemporaryDirectory(
            "nofarma-opposite-key-utf16");
        string publish = Path.Combine(directory.Path, "publish");
        Directory.CreateDirectory(publish);
        using ECDsa oppositeKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] oppositePublicKey = oppositeKey.ExportSubjectPublicKeyInfo();
        string oppositeBase64 = Convert.ToBase64String(oppositePublicKey);
        string oppositePath = Path.Combine(directory.Path, "opposite.spki.b64");
        File.WriteAllText(oppositePath, oppositeBase64);
        var encoding = new UnicodeEncoding(
            bigEndian: false,
            byteOrderMark: true);
        byte[] encodedBase64 = encoding.GetBytes(oppositeBase64);
        int markerStart = (64 * 1024) - (encodedBase64.Length / 2);
        byte[] contents = Enumerable
            .Repeat((byte)0x7F, markerStart + encodedBase64.Length + 32)
            .ToArray();
        encoding.GetPreamble().CopyTo(contents, 0);
        encodedBase64.CopyTo(contents, markerStart);
        File.WriteAllBytes(Path.Combine(publish, "renamed.bin"), contents);

        LicenseChannelKeyValidation result =
            QaPublishedOutputScanner.Validate(publish, oppositePath);

        Assert.False(result.IsValid);
        Assert.Equal("NFLC010", result.Code);
    }

    [Theory]
    [MemberData(nameof(EncodedTextCases))]
    public void PublishedOutputScannerRejectsRenamedPuttyPrivateKey(
        string encodingName,
        bool includeBom)
    {
        using var output = new SafeTemporaryDirectory(
            "nofarma-putty-private-key");
        Encoding encoding = MarkerEncoding(encodingName, includeBom);
        string putty = string.Join(
            "\r\n",
            string.Concat("PuTTY-User-", "Key-File-3: ssh-ed25519"),
            "Encryption: none",
            "Comment: regression",
            "Public-Lines: 1",
            "AAAA",
            string.Concat("Private-", "Lines: 1"),
            "AAAA");
        byte[] marker = encoding.GetBytes(putty);
        byte[] preamble = includeBom ? encoding.GetPreamble() : [];
        int markerStart = (64 * 1024) - Math.Max(1, marker.Length / 3);
        byte[] contents = Enumerable
            .Repeat((byte)0x7F, markerStart + marker.Length + 32)
            .ToArray();
        preamble.CopyTo(contents, 0);
        marker.CopyTo(contents, markerStart);
        File.WriteAllBytes(Path.Combine(output.Path, "renamed.bin"), contents);

        LicenseChannelKeyValidation result =
            QaPublishedOutputScanner.Validate(output.Path);

        Assert.False(result.IsValid);
        Assert.Equal("NFLC010", result.Code);
    }

    [Fact]
    public void PublishedOutputScannerAcceptsDistantPuttyMarkers()
    {
        using var output = new SafeTemporaryDirectory(
            "nofarma-distant-putty-markers");
        string contents = string.Concat(
            string.Concat("PuTTY-User-", "Key-File-3: ssh-ed25519"),
            new string('A', 20 * 1024),
            string.Concat("Private-", "Lines: 1"));
        File.WriteAllText(Path.Combine(output.Path, "renamed.bin"), contents);

        LicenseChannelKeyValidation result =
            QaPublishedOutputScanner.Validate(output.Path);

        Assert.True(result.IsValid, result.Message);
    }

    [Theory]
    [MemberData(nameof(OppositeKeyTextCases))]
    public void PublishedOutputScannerRejectsNormalizedOppositeKeyInText(
        string encodingName,
        bool includeBom,
        bool includeWhitespace)
    {
        using var directory = new SafeTemporaryDirectory(
            "nofarma-normalized-opposite-key");
        string publish = Path.Combine(directory.Path, "publish");
        Directory.CreateDirectory(publish);
        using ECDsa oppositeKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] oppositePublicKey = oppositeKey.ExportSubjectPublicKeyInfo();
        string oppositeBase64 = Convert.ToBase64String(oppositePublicKey);
        string oppositePath = Path.Combine(directory.Path, "opposite.spki.b64");
        File.WriteAllText(oppositePath, oppositeBase64);
        string represented = includeWhitespace
            ? AddArbitraryWhitespace(oppositeBase64)
            : oppositeBase64;
        Encoding encoding = MarkerEncoding(encodingName, includeBom);
        byte[] preamble = includeBom ? encoding.GetPreamble() : [];
        File.WriteAllBytes(
            Path.Combine(publish, "renamed.txt"),
            [.. preamble, .. encoding.GetBytes(represented)]);

        LicenseChannelKeyValidation result =
            QaPublishedOutputScanner.Validate(publish, oppositePath);

        Assert.False(result.IsValid);
        Assert.Equal("NFLC010", result.Code);
    }

    private static string AddArbitraryWhitespace(string value)
    {
        var result = new StringBuilder(value.Length + 32);
        for (int index = 0; index < value.Length; index++)
        {
            result.Append(value[index]);
            if ((index + 1) % 13 == 0)
            {
                result.Append("\r\n \t");
            }
        }

        return result.ToString();
    }

    private static Encoding MarkerEncoding(
        string name,
        bool includeBom) => name switch
        {
            "utf8" => new UTF8Encoding(includeBom),
            "utf16le" => new UnicodeEncoding(
                bigEndian: false,
                byteOrderMark: includeBom),
            "utf16be" => new UnicodeEncoding(
                bigEndian: true,
                byteOrderMark: includeBom),
            "utf32le" => new UTF32Encoding(
                bigEndian: false,
                byteOrderMark: includeBom),
            "utf32be" => new UTF32Encoding(
                bigEndian: true,
                byteOrderMark: includeBom),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
}
