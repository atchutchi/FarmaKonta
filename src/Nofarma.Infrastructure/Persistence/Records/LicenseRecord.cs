namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class LicenseRecord
{
    public Guid Id { get; set; }

    public Guid InstallationId { get; set; }

    public Guid LicenseId { get; set; }

    public int Plan { get; set; }

    public long Sequence { get; set; }

    public string Channel { get; set; } = string.Empty;

    public string KeyId { get; set; } = string.Empty;

    public byte[] DocumentBytes { get; set; } = [];

    public byte[] DocumentHash { get; set; } = [];

    public DateTimeOffset IssuedAtUtc { get; set; }

    public DateTimeOffset ValidFromUtc { get; set; }

    public DateTimeOffset ValidUntilUtc { get; set; }

    public DateTimeOffset GraceUntilUtc { get; set; }

    public DateTimeOffset ImportedAtUtc { get; set; }

    public InstallationRecord Installation { get; set; } = null!;
}
