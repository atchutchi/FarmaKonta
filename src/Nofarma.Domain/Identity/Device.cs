using Nofarma.Domain.Common;

namespace Nofarma.Domain.Identity;

public sealed class Device
{
    public Device(EntityId id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name.Trim();
    }

    public EntityId Id { get; }

    public string Name { get; }
}
