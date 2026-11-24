using Curvy.Core.Model;
using System.Threading.Tasks;

namespace Curvy.Core.Tests.Model;

public sealed class ProfileNamesTests
{
    [Test]
    public async Task Unique_ReturnsBaseName_WhenFree() =>
        await Assert.That(ProfileNames.Unique([Profile.New("Other")], "HD 600")).IsEqualTo("HD 600");

    [Test]
    public async Task Unique_AppendsNumber_IgnoringCase()
    {
        var existing = new[] { Profile.New("hd 600"), Profile.New("HD 600 2") };
        await Assert.That(ProfileNames.Unique(existing, "HD 600")).IsEqualTo("HD 600 3");
    }
}