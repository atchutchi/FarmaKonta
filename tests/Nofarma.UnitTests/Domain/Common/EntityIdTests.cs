using Nofarma.Domain.Common;

namespace Nofarma.UnitTests.Domain.Common;

public sealed class EntityIdTests
{
    [Fact]
    public void NewNeverReturnsEmptyGuid()
    {
        EntityId id = EntityId.New();

        Assert.NotEqual(Guid.Empty, id.Value);
    }
}
