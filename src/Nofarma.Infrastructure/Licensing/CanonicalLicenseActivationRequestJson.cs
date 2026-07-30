using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using Nofarma.Application.Licensing;

namespace Nofarma.Infrastructure.Licensing;

public sealed record LicenseActivationRequestEnvelope(
    int SchemaVersion,
    LicenseBuildChannel Channel,
    Guid PharmacyId,
    Guid EstablishmentId,
    Guid DeviceId,
    string DeviceKeyThumbprint);

public static class CanonicalLicenseActivationRequestJson
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumDocumentBytes = 4 * 1024;

    private const int MaximumDepth = 4;
    private const string ThumbprintPrefix = "SHA256:";
    private const int Sha256HexCharacters = 64;

    private static readonly HashSet<string> PropertyNames = new(StringComparer.Ordinal)
    {
        "schemaVersion",
        "channel",
        "pharmacyId",
        "establishmentId",
        "deviceId",
        "deviceKeyThumbprint"
    };

    public static byte[] Serialize(
        LicenseActivationRequest request,
        LicenseBuildChannel channel)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateChannel(channel);
        ValidateIdentity(request);

        var envelope = new LicenseActivationRequestEnvelope(
            CurrentSchemaVersion,
            channel,
            request.Context.PharmacyId.Value,
            request.Context.EstablishmentId.Value,
            request.Context.DeviceId.Value,
            request.Device.PublicKeyThumbprint);
        byte[] document = Serialize(envelope);
        if (document.Length > MaximumDocumentBytes)
        {
            throw new InvalidOperationException("The activation request is too large.");
        }

        return document;
    }

    public static bool TryParse(
        ReadOnlyMemory<byte> document,
        out LicenseActivationRequestEnvelope? envelope)
    {
        envelope = null;
        if (document.IsEmpty || document.Length > MaximumDocumentBytes)
        {
            return false;
        }

        try
        {
            using JsonDocument json = JsonDocument.Parse(
                document,
                new JsonDocumentOptions
                {
                    MaxDepth = MaximumDepth,
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow
                });
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasExactProperties(root))
            {
                return false;
            }

            if (!root.GetProperty("schemaVersion").TryGetInt32(out int schemaVersion)
                || schemaVersion != CurrentSchemaVersion
                || !root.GetProperty("channel").TryGetInt32(out int channelValue)
                || !Enum.IsDefined((LicenseBuildChannel)channelValue)
                || !TryReadGuid(root, "pharmacyId", out Guid pharmacyId)
                || !TryReadGuid(root, "establishmentId", out Guid establishmentId)
                || !TryReadGuid(root, "deviceId", out Guid deviceId)
                || !TryReadThumbprint(root, out string? thumbprint)
                || pharmacyId == Guid.Empty
                || establishmentId != pharmacyId
                || establishmentId == Guid.Empty
                || deviceId == Guid.Empty)
            {
                return false;
            }

            var parsed = new LicenseActivationRequestEnvelope(
                schemaVersion,
                (LicenseBuildChannel)channelValue,
                pharmacyId,
                establishmentId,
                deviceId,
                thumbprint!);
            byte[] canonical = Serialize(parsed);
            if (!document.Span.SequenceEqual(canonical))
            {
                return false;
            }

            envelope = parsed;
            return true;
        }
        catch (Exception exception) when (
            exception is JsonException
                or InvalidOperationException
                or FormatException
                or OverflowException
                or ArgumentException)
        {
            return false;
        }
    }

    private static byte[] Serialize(LicenseActivationRequestEnvelope envelope)
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
            writer.WriteString("pharmacyId", CanonicalGuid(envelope.PharmacyId));
            writer.WriteString("establishmentId", CanonicalGuid(envelope.EstablishmentId));
            writer.WriteString("deviceId", CanonicalGuid(envelope.DeviceId));
            writer.WriteString("deviceKeyThumbprint", envelope.DeviceKeyThumbprint);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void ValidateChannel(LicenseBuildChannel channel)
    {
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(nameof(channel));
        }
    }

    private static void ValidateIdentity(LicenseActivationRequest request)
    {
        if (request.Context.PharmacyId.Value == Guid.Empty
            || request.Context.EstablishmentId.Value == Guid.Empty
            || request.Context.DeviceId.Value == Guid.Empty)
        {
            throw new ArgumentException("The activation request identifiers cannot be empty.", nameof(request));
        }

        if (request.Context.PharmacyId != request.Device.PharmacyId
            || request.Context.EstablishmentId != request.Context.PharmacyId
            || request.Context.DeviceId != request.Device.DeviceId)
        {
            throw new ArgumentException("The activation request identity does not match its context.", nameof(request));
        }

        if (!IsCanonicalThumbprint(request.Device.PublicKeyThumbprint))
        {
            throw new ArgumentException("The activation request device thumbprint is invalid.", nameof(request));
        }
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

    private static bool TryReadGuid(JsonElement root, string propertyName, out Guid value)
    {
        value = default;
        JsonElement property = root.GetProperty(propertyName);
        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? text = property.GetString();
        return text is not null
            && Guid.TryParseExact(text, "D", out value)
            && string.Equals(text, CanonicalGuid(value), StringComparison.Ordinal);
    }

    private static bool TryReadThumbprint(JsonElement root, out string? thumbprint)
    {
        JsonElement property = root.GetProperty("deviceKeyThumbprint");
        thumbprint = property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
        return IsCanonicalThumbprint(thumbprint);
    }

    private static bool IsCanonicalThumbprint(string? thumbprint)
    {
        if (thumbprint is null
            || thumbprint.Length != ThumbprintPrefix.Length + Sha256HexCharacters
            || !thumbprint.StartsWith(ThumbprintPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char character in thumbprint.AsSpan(ThumbprintPrefix.Length))
        {
            if (character is not (>= '0' and <= '9')
                and not (>= 'A' and <= 'F'))
            {
                return false;
            }
        }

        return true;
    }

    private static string CanonicalGuid(Guid value) => value.ToString("D");
}
