using Curvy.Core.Model;
using Xunit;

namespace Curvy.Core.Tests.Model;

public sealed class ProfileNamesTests
{
    [Fact]
    public void Unique_ReturnsBaseName_WhenFree() =>
        Assert.Equal("HD 600", ProfileNames.Unique([Profile.New("Other")], "HD 600"));

    [Fact]
    public void Unique_AppendsNumber_IgnoringCase()
    {
        var existing = new[] { Profile.New("hd 600"), Profile.New("HD 600 2") };
        Assert.Equal("HD 600 3", ProfileNames.Unique(existing, "HD 600"));
    }
}
