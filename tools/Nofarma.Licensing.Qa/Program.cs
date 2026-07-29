using System.Globalization;
using System.Security.Cryptography;
using Nofarma.Domain.Licensing;

namespace Nofarma.Licensing.Qa;

internal static class Program
{
    private static int Main(string[] args) =>
        QaCli.Run(args, Console.In, Console.Out, Console.Error);
}

public sealed class QaIssuerException(
    string code,
    string message,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public sealed record LicenseChannelKeyValidation(
    bool IsValid,
    string? Code,
    string Message);

public sealed record QaDecodedPublicKey(
    int BytesRead,
    int KeySize,
    string? CurveOid,
    byte[] X,
    byte[] Y,
    byte[] CanonicalSubjectPublicKey);

public interface IQaPublicKeyDecoder
{
    QaDecodedPublicKey Decode(ReadOnlyMemory<byte> subjectPublicKey);
}

public static class QaPublicKeyValidator
{
    private const int MaximumPublicKeyFileBytes = 8 * 1024;
    private const string NistP256Oid = "1.2.840.10045.3.1.7";

    internal static IQaPublicKeyDecoder DefaultDecoder =>
        DefaultPublicKeyDecoder.Instance;

    public static LicenseChannelKeyValidation ValidateCommercial(
        string qaPublicKeyPath,
        string commercialPublicKeyPath)
    {
        LicenseChannelKeyValidation result = ValidateCommercialCore(
            qaPublicKeyPath,
            commercialPublicKeyPath,
            DefaultPublicKeyDecoder.Instance,
            out _);
        return result;
    }

    public static LicenseChannelKeyValidation ValidateCommercial(
        string qaPublicKeyPath,
        string commercialPublicKeyPath,
        IQaPublicKeyDecoder decoder)
    {
        ArgumentNullException.ThrowIfNull(decoder);
        return ValidateCommercialCore(
            qaPublicKeyPath,
            commercialPublicKeyPath,
            decoder,
            out _);
    }

    public static LicenseChannelKeyValidation ValidateCommercialToFile(
        string qaPublicKeyPath,
        string commercialPublicKeyPath,
        string validatedOutputPath)
    {
        LicenseChannelKeyValidation result = ValidateCommercialCore(
            qaPublicKeyPath,
            commercialPublicKeyPath,
            DefaultPublicKeyDecoder.Instance,
            out byte[] commercialSubjectPublicKey);
        if (!result.IsValid)
        {
            return result;
        }

        try
        {
            WriteCanonicalPublicKey(validatedOutputPath, commercialSubjectPublicKey);
            return result;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException)
        {
            return new LicenseChannelKeyValidation(
                false,
                "NFLC004",
                "The validated Commercial public key output is unavailable.");
        }
    }

    private static LicenseChannelKeyValidation ValidateCommercialCore(
        string qaPublicKeyPath,
        string commercialPublicKeyPath,
        IQaPublicKeyDecoder decoder,
        out byte[] commercialSubjectPublicKey)
    {
        commercialSubjectPublicKey = Array.Empty<byte>();
        if (!TryReadP256(
                qaPublicKeyPath,
                decoder,
                out ECParameters qa,
                out _,
                out string? qaFailure))
        {
            return new LicenseChannelKeyValidation(
                false,
                "NFLC003",
                qaFailure ?? "QA public key validation failed.");
        }

        if (!TryReadP256(
                commercialPublicKeyPath,
                decoder,
                out ECParameters commercial,
                out commercialSubjectPublicKey,
                out string? commercialFailure))
        {
            return new LicenseChannelKeyValidation(
                false,
                "NFLC004",
                commercialFailure ?? "Commercial public key validation failed.");
        }

        bool equal = qa.Q.X is not null
            && qa.Q.Y is not null
            && commercial.Q.X is not null
            && commercial.Q.Y is not null
            && qa.Q.X.Length == commercial.Q.X.Length
            && qa.Q.Y.Length == commercial.Q.Y.Length
            && CryptographicOperations.FixedTimeEquals(qa.Q.X, commercial.Q.X)
            && CryptographicOperations.FixedTimeEquals(qa.Q.Y, commercial.Q.Y);
        return equal
            ? new LicenseChannelKeyValidation(
                false,
                "NFLC002",
                "Commercial public key is cryptographically identical to the QA public key.")
            : new LicenseChannelKeyValidation(
                true,
                null,
                "Commercial and QA public keys are valid and distinct.");
    }

    private static void WriteCanonicalPublicKey(
        string outputPath,
        byte[] subjectPublicKey)
    {
        string fullOutputPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullOutputPath);
        if (directory is null)
        {
            throw new ArgumentException("The validated public key output path is invalid.");
        }

        Directory.CreateDirectory(directory);
        byte[] encoded = System.Text.Encoding.ASCII.GetBytes(
            string.Concat(
                Convert.ToBase64String(subjectPublicKey),
                Environment.NewLine));
        bool created = false;
        try
        {
            using (new FileStream(
                       fullOutputPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
            }

            created = true;
            QaFileSecurity.ProtectForCurrentUser(fullOutputPath);
            using var stream = new FileStream(
                fullOutputPath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.None);
            stream.Write(encoded);
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            if (created && File.Exists(fullOutputPath))
            {
                File.Delete(fullOutputPath);
            }

            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encoded);
        }
    }

    private static bool TryReadP256(
        string path,
        IQaPublicKeyDecoder decoder,
        out ECParameters parameters,
        out byte[] canonicalSubjectPublicKey,
        out string? failure)
    {
        parameters = default;
        canonicalSubjectPublicKey = Array.Empty<byte>();
        failure = null;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                failure = "The public key path is required.";
                return false;
            }

            string fullPath = Path.GetFullPath(path);
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            if (stream.Length < 1 || stream.Length > MaximumPublicKeyFileBytes)
            {
                failure = "The public key file size is invalid.";
                return false;
            }

            byte[] encoded = new byte[checked((int)stream.Length)];
            stream.ReadExactly(encoded);
            byte[] subjectPublicKey = Array.Empty<byte>();
            try
            {
                string base64 = System.Text.Encoding.ASCII.GetString(encoded);
                subjectPublicKey = Convert.FromBase64String(base64);
                QaDecodedPublicKey decoded = decoder.Decode(subjectPublicKey);
                if (decoded.BytesRead != subjectPublicKey.Length
                    || decoded.KeySize != 256
                    || decoded.X is not { Length: 32 }
                    || decoded.Y is not { Length: 32 }
                    || !string.Equals(
                        decoded.CurveOid,
                        NistP256Oid,
                        StringComparison.Ordinal))
                {
                    parameters = default;
                    failure = "The public key must be an ECDSA NIST P-256 SPKI.";
                    return false;
                }

                parameters = new ECParameters
                {
                    Curve = ECCurve.CreateFromValue(decoded.CurveOid!),
                    Q = new ECPoint
                    {
                        X = decoded.X,
                        Y = decoded.Y
                    }
                };
                canonicalSubjectPublicKey = decoded.CanonicalSubjectPublicKey.ToArray();
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encoded);
                if (subjectPublicKey.Length > 0)
                {
                    CryptographicOperations.ZeroMemory(subjectPublicKey);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or FormatException
                or CryptographicException
                or PlatformNotSupportedException)
        {
            parameters = default;
            canonicalSubjectPublicKey = Array.Empty<byte>();
            failure = "The public key file is unavailable or invalid.";
            return false;
        }
    }

    private sealed class DefaultPublicKeyDecoder : IQaPublicKeyDecoder
    {
        internal static readonly DefaultPublicKeyDecoder Instance = new();

        public QaDecodedPublicKey Decode(ReadOnlyMemory<byte> subjectPublicKey)
        {
            using ECDsa key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(subjectPublicKey.Span, out int bytesRead);
            ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
            return new QaDecodedPublicKey(
                bytesRead,
                key.KeySize,
                parameters.Curve.Oid.Value,
                parameters.Q.X ?? Array.Empty<byte>(),
                parameters.Q.Y ?? Array.Empty<byte>(),
                key.ExportSubjectPublicKeyInfo());
        }
    }
}

public static class QaCli
{
    public static int Run(
        IReadOnlyList<string> args,
        TextReader input,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            if (args.Count == 0)
            {
                throw Usage("A command is required.");
            }

            string command = args[0];
            OptionSet options = OptionSet.Parse(args.Skip(1));
            return command switch
            {
                "provision" => RunProvision(options, input, output),
                "issue" => RunIssue(options, output),
                "validate-commercial-key" => RunCommercialValidation(options, error),
                _ => throw Usage("The command is not supported.")
            };
        }
        catch (QaIssuerException exception)
        {
            error.WriteLine($"{exception.Code}: {exception.Message}");
            return 2;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or CryptographicException
                or ArgumentException)
        {
            error.WriteLine($"QA_ISSUER_FAILED: {exception.Message}");
            return 3;
        }
    }

    private static int RunProvision(
        OptionSet options,
        TextReader input,
        TextWriter output)
    {
        options.RequireOnly("public-output", "rotate");
        string publicOutputPath = options.Required("public-output");
        bool rotate = options.HasFlag("rotate");
        if (!OperatingSystem.IsWindows())
        {
            throw new QaIssuerException(
                "QA_WINDOWS_REQUIRED",
                "QA key provisioning requires Windows DPAPI CurrentUser.");
        }

        if (rotate)
        {
            output.WriteLine(
                $"Type {QaKeyStore.RotationConfirmation} to confirm QA key rotation:");
        }

        QaKeyStore.CreateDefault().Provision(publicOutputPath, rotate, input);
        output.WriteLine("QA public key provisioned.");
        return 0;
    }

    private static int RunIssue(OptionSet options, TextWriter output)
    {
        options.RequireOnly(
            "request",
            "plan",
            "valid-from",
            "valid-until",
            "output");
        string requestPath = options.Required("request");
        LicensePlan plan = ParsePlan(options.Required("plan"));
        DateTimeOffset validFromUtc = ParseDate(
            options.Required("valid-from"),
            "valid-from");
        DateTimeOffset validUntilUtc = options.Optional("valid-until") is { } until
            ? ParseDate(until, "valid-until")
            : validFromUtc.AddDays(plan == LicensePlan.Monthly ? 31 : 366);
        string outputPath = options.Required("output");
        if (!OperatingSystem.IsWindows())
        {
            throw new QaIssuerException(
                "QA_WINDOWS_REQUIRED",
                "QA licence issuance requires Windows DPAPI CurrentUser.");
        }

        byte[] request = ReadRequest(requestPath);
        using ECDsa signingKey = QaKeyStore.CreateDefault().OpenSigningKey();
        var issuer = new QaLicenseIssuer(signingKey);
        issuer.IssueToFile(request, plan, validFromUtc, validUntilUtc, outputPath);
        output.WriteLine("QA licence issued.");
        return 0;
    }

    private static int RunCommercialValidation(OptionSet options, TextWriter error)
    {
        options.RequireOnly("qa-public", "commercial-public", "validated-output");
        LicenseChannelKeyValidation result =
            QaPublicKeyValidator.ValidateCommercialToFile(
                options.Required("qa-public"),
                options.Required("commercial-public"),
                options.Required("validated-output"));
        if (result.IsValid)
        {
            return 0;
        }

        error.WriteLine($"{result.Code}: {result.Message}");
        return 2;
    }

    private static byte[] ReadRequest(string path)
    {
        string fullPath = Path.GetFullPath(path);
        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length < 1
            || stream.Length > Nofarma.Infrastructure.Licensing
                .CanonicalLicenseActivationRequestJson.MaximumDocumentBytes)
        {
            throw new QaIssuerException(
                "QA_REQUEST_INVALID",
                "The activation request size is invalid.");
        }

        byte[] request = new byte[checked((int)stream.Length)];
        stream.ReadExactly(request);
        return request;
    }

    private static LicensePlan ParsePlan(string value)
    {
        if (string.Equals(value, "Monthly", StringComparison.OrdinalIgnoreCase))
        {
            return LicensePlan.Monthly;
        }

        if (string.Equals(value, "Annual", StringComparison.OrdinalIgnoreCase))
        {
            return LicensePlan.Annual;
        }

        throw new QaIssuerException(
            "PLAN_INVALID",
            "The plan must be Monthly or Annual.");
    }

    private static DateTimeOffset ParseDate(string value, string optionName)
    {
        string? datePart = value.EndsWith('Z')
            ? value[..^1]
            : value.EndsWith("+00:00", StringComparison.Ordinal)
                ? value[..^6]
                : null;
        string[] formats =
        [
            "yyyy-MM-dd'T'HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"
        ];
        if (datePart is null
            || !DateTime.TryParseExact(
                datePart,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime parsed))
        {
            throw new QaIssuerException(
                "VALIDITY_DATES_INVALID",
                $"The {optionName} date is invalid.");
        }

        return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
    }

    private static QaIssuerException Usage(string message) =>
        new(
            "QA_USAGE_INVALID",
            string.Concat(
                message,
                " Use provision --public-output <path> [--rotate] or ",
                "issue --request <path> --plan <Monthly|Annual> ",
                "--valid-from <UTC> [--valid-until <UTC>] --output <path>."));

    private sealed class OptionSet
    {
        private readonly Dictionary<string, string?> _values =
            new(StringComparer.Ordinal);

        private OptionSet()
        {
        }

        internal static OptionSet Parse(IEnumerable<string> arguments)
        {
            string[] values = arguments.ToArray();
            var parsed = new OptionSet();
            for (int index = 0; index < values.Length; index++)
            {
                string token = values[index];
                if (!token.StartsWith("--", StringComparison.Ordinal)
                    || token.Length == 2)
                {
                    throw Usage("Each option must start with --.");
                }

                string name = token[2..];
                string? value = null;
                if (!string.Equals(name, "rotate", StringComparison.Ordinal))
                {
                    if (index + 1 >= values.Length
                        || values[index + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        throw Usage($"The --{name} option requires a value.");
                    }

                    value = values[++index];
                }

                if (!parsed._values.TryAdd(name, value))
                {
                    throw Usage($"The --{name} option was repeated.");
                }
            }

            return parsed;
        }

        internal string Required(string name) =>
            _values.TryGetValue(name, out string? value)
                && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw Usage($"The --{name} option is required.");

        internal string? Optional(string name) =>
            _values.TryGetValue(name, out string? value)
                ? value
                : null;

        internal bool HasFlag(string name) =>
            _values.TryGetValue(name, out string? value) && value is null;

        internal void RequireOnly(params string[] allowed)
        {
            var allowedNames = new HashSet<string>(allowed, StringComparer.Ordinal);
            string? unknown = _values.Keys.FirstOrDefault(
                name => !allowedNames.Contains(name));
            if (unknown is not null)
            {
                throw Usage($"The --{unknown} option is not supported.");
            }
        }
    }
}
