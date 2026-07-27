namespace Nofarma.Infrastructure.Persistence.Records;

public sealed class RecoveryCodeRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public int Version { get; set; }

    public string Algorithm { get; set; } = string.Empty;

    public int WorkFactor { get; set; }

    public byte[] Salt { get; set; } = [];

    public byte[] Hash { get; set; } = [];

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? UsedAtUtc { get; set; }
}
