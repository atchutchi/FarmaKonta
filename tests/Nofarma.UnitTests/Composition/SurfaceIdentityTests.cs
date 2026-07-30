using Nofarma.Contracts.Diagnostics;

namespace Nofarma.UnitTests.Composition;

public sealed class SurfaceIdentityTests
{
    [Fact]
    public void ServiceNamesRemainStableAcrossSurfaces()
    {
        Assert.Equal("Nofarma.Desktop", ServiceNames.Desktop);
        Assert.Equal("Nofarma.Api", ServiceNames.Api);
        Assert.Equal("Nofarma.AdminWeb", ServiceNames.AdminWeb);
        Assert.Equal("Nofarma.Sync", ServiceNames.Sync);
    }
}
