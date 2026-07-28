using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Nofarma.Domain.Licensing;

namespace Nofarma.Infrastructure.Licensing;

public sealed record SignedLicenseEnvelope(
    int SchemaVersion,
    LicenseBuildChannel Channel,
    string KeyId,
    int SignatureAlgorithm,
    Guid LicenseId,
    Guid PharmacyId,
    Guid EstablishmentId,
    Guid DeviceId,
    string DeviceKeyThumbprint,
    LicensePlan Plan,
    long Sequence,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc,
    DateTimeOffset GraceUntilUtc,
    IReadOnlyList<int> Capabilities,
    ReadOnlyMemory<byte> Signature);

public static class CanonicalLicenseJson
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        MaxDepth = 16,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow
    };

    private static readonly HashSet<string> PropertyNames = new(StringComparer.Ordinal)
    {
        "schemaVersion",
        "channel",
        "keyId",
        "signatureAlgorithm",
        "licenseId",
        "pharmacyId",
        "establishmentId",
        "deviceId",
        "deviceKeyThumbprint",
        "plan",
        "sequence",
        "issuedAtUtc",
        "validFromUtc",
        "validUntilUtc",
        "graceUntilUtc",
        "capabilities",
        "signature"
    };

    public static byte[] SerializePayload(SignedLicenseEnvelope envelope) =>
        Serialize(envelope ?? throw new ArgumentNullException(nameof(envelope)), includeSignature: false);

    public static byte[] SerializeEnvelope(SignedLicenseEnvelope envelope) =>
        Serialize(envelope ?? throw new ArgumentNullException(nameof(envelope)), includeSignature: true);

    internal static bool TryParseEnvelope(
        ReadOnlyMemory<byte> document,
        out SignedLicenseEnvelope? envelope)
    {
        envelope = null;

        try
        {
            using JsonDocument json = JsonDocument.Parse(document, DocumentOptions);
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasExactProperties(root))
            {
                return false;
            }

            if (!TryReadInt32(root, "schemaVersion", out int schemaVersion)
                || !TryReadInt32(root, "channel", out int channel)
                || !TryReadNonEmptyString(root, "keyId", 128, out string? keyId)
                || !TryReadInt32(root, "signatureAlgorithm", out int signatureAlgorithm)
                || !TryReadGuid(root, "licenseId", out Guid licenseId)
                || !TryReadGuid(root, "pharmacyId", out Guid pharmacyId)
                || !TryReadGuid(root, "establishmentId", out Guid establishmentId)
                || !TryReadGuid(root, "deviceId", out Guid deviceId)
                || !TryReadNonEmptyString(
                    root,
                    "deviceKeyThumbprint",
                    512,
                    out string? deviceKeyThumbprint)
                || !TryReadInt32(root, "plan", out int plan)
                || !TryReadInt64(root, "sequence", out long sequence)
                || !TryReadUtcDate(root, "issuedAtUtc", out DateTimeOffset issuedAtUtc)
                || !TryReadUtcDate(root, "validFromUtc", out DateTimeOffset validFromUtc)
                || !TryReadUtcDate(root, "validUntilUtc", out DateTimeOffset validUntilUtc)
                || !TryReadUtcDate(root, "graceUntilUtc", out DateTimeOffset graceUntilUtc)
                || !TryReadCapabilities(root, out int[]? capabilities)
                || !TryReadSignature(root, out byte[]? signature))
            {
                return false;
            }

            envelope = new SignedLicenseEnvelope(
                schemaVersion,
                (LicenseBuildChannel)channel,
                keyId!,
                signatureAlgorithm,
                licenseId,
                pharmacyId,
                establishmentId,
                deviceId,
                deviceKeyThumbprint!,
                (LicensePlan)plan,
                sequence,
                issuedAtUtc,
                validFromUtc,
                validUntilUtc,
                graceUntilUtc,
                capabilities!,
                signature!);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static byte[] Serialize(SignedLicenseEnvelope envelope, bool includeSignature)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions
            {
                Indented = false,
                SkipValidation = false,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", envelope.SchemaVersion);
            writer.WriteNumber("channel", (int)envelope.Channel);
            writer.WriteString("keyId", envelope.KeyId);
            writer.WriteNumber("signatureAlgorithm", envelope.SignatureAlgorithm);
            writer.WriteString("licenseId", CanonicalGuid(envelope.LicenseId));
            writer.WriteString("pharmacyId", CanonicalGuid(envelope.PharmacyId));
            writer.WriteString("establishmentId", CanonicalGuid(envelope.EstablishmentId));
            writer.WriteString("deviceId", CanonicalGuid(envelope.DeviceId));
            writer.WriteString("deviceKeyThumbprint", envelope.DeviceKeyThumbprint);
            writer.WriteNumber("plan", (int)envelope.Plan);
            writer.WriteNumber("sequence", envelope.Sequence);
            writer.WriteString("issuedAtUtc", CanonicalUtc(envelope.IssuedAtUtc));
            writer.WriteString("validFromUtc", CanonicalUtc(envelope.ValidFromUtc));
            writer.WriteString("validUntilUtc", CanonicalUtc(envelope.ValidUntilUtc));
            writer.WriteString("graceUntilUtc", CanonicalUtc(envelope.GraceUntilUtc));
            writer.WriteStartArray("capabilities");
            foreach (int capability in envelope.Capabilities)
            {
                writer.WriteNumberValue(capability);
            }

            writer.WriteEndArray();
            if (includeSignature)
            {
                writer.WriteBase64String("signature", envelope.Signature.Span);
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static bool HasExactProperties(JsonElement root)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!PropertyNames.Contains(property.Name) || !found.Add(property.Name))
            {
                return false;
            }
        }

        return found.Count == PropertyNames.Count;
    }

    private static bool TryReadInt32(JsonElement root, string name, out int value) =>
        root.GetProperty(name).TryGetInt32(out value);

    private static bool TryReadInt64(JsonElement root, string name, out long value) =>
        root.GetProperty(name).TryGetInt64(out value);

    private static bool TryReadNonEmptyString(
        JsonElement root,
        string name,
        int maximumLength,
        out string? value)
    {
        JsonElement property = root.GetProperty(name);
        value = property.ValueKind == JsonValueKind.String ? property.GetString() : null;
        return !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;
    }

    private static bool TryReadGuid(JsonElement root, string name, out Guid value)
    {
        value = default;
        JsonElement property = root.GetProperty(name);
        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? text = property.GetString();
        return text is not null
            && Guid.TryParseExact(text, "D", out value)
            && string.Equals(text, CanonicalGuid(value), StringComparison.Ordinal);
    }

    private static bool TryReadUtcDate(
        JsonElement root,
        string name,
        out DateTimeOffset value)
    {
        value = default;
        JsonElement property = root.GetProperty(name);
        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? text = property.GetString();
        return text is not null
            && DateTimeOffset.TryParseExact(
                text,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out value)
            && value.Offset == TimeSpan.Zero
            && string.Equals(text, CanonicalUtc(value), StringComparison.Ordinal);
    }

    private static bool TryReadCapabilities(JsonElement root, out int[]? capabilities)
    {
        capabilities = null;
        JsonElement property = root.GetProperty("capabilities");
        if (property.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var values = new List<int>();
        foreach (JsonElement item in property.EnumerateArray())
        {
            if (!item.TryGetInt32(out int value))
            {
                return false;
            }

            values.Add(value);
        }

        capabilities = values.ToArray();
        return true;
    }

    private static bool TryReadSignature(JsonElement root, out byte[]? signature)
    {
        signature = null;
        JsonElement property = root.GetProperty("signature");
        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? text = property.GetString();
        if (text is null)
        {
            return false;
        }

        signature = Convert.FromBase64String(text);
        return true;
    }

    private static string CanonicalGuid(Guid value) => value.ToString("D");

    private static string CanonicalUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
