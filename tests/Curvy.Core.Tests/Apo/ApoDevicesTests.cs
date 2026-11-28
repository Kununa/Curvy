using System.Threading.Tasks;
using Curvy.Core.Apo;

namespace Curvy.Core.Tests.Apo;

public sealed class ApoDevicesTests
{
    [Test]
    public async Task EndpointGuid_ReadsTheGuidAfterTheFlowPrefix()
    {
        var id = "{0.0.0.00000000}.{1eda1038-37bc-40d3-a90e-c540cc764bf3}";
        await Assert.That(ApoDevices.EndpointGuid(id)).IsEqualTo(Guid.Parse("1eda1038-37bc-40d3-a90e-c540cc764bf3"));
    }

    [Test]
    [Arguments("")]
    [Arguments("{0.0.0.00000000}")]
    [Arguments("{0.0.0.00000000}.not-a-guid")]
    public async Task EndpointGuid_IsNullForOtherIds(string id)
    {
        await Assert.That(ApoDevices.EndpointGuid(id)).IsNull();
    }
}
