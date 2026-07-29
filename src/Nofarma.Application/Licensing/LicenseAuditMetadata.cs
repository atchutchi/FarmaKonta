using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Nofarma.Domain.Licensing;

namespace Nofarma.Application.Licensing;

public static class LicenseAuditMetadata
{
    public static string Serialize(LicenseGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("sequence", grant.Sequence);
            writer.WriteString("plan", grant.Plan.ToString());
            writer.WriteString(
                "validFromUtc",
                grant.ValidFromUtc.Value.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteString(
                "validUntilUtc",
                grant.ValidUntilUtc.Value.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
