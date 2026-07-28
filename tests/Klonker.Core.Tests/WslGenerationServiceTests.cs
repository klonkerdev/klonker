using System.Text;
using Klonker.Desktop.Services;

namespace Klonker.Core.Tests;

public sealed class WslGenerationServiceTests
{
    [Fact]
    public void DestinationMapsAbsoluteLinuxPathToWslLocalhost()
    {
        var service = new WslGenerationService();

        var result = service.ResolveDestination(
            "Ubuntu-24.04",
            "/home/alice/projects/demo");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            @"\\wsl.localhost\Ubuntu-24.04\home\alice\projects\demo",
            result.Value!.WindowsUncPath);
        Assert.Equal(
            "/home/alice/projects/demo",
            result.Value.LinuxPath);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/")]
    [InlineData("/home/alice/../escape")]
    [InlineData("/home/alice/bad:name")]
    public void DestinationRejectsUnsafeOrWindowsIncompatiblePath(string path)
    {
        var result = new WslGenerationService().ResolveDestination(
            "Ubuntu",
            path);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void DistributionParserHandlesWslUtf16Output()
    {
        var bytes = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes(
                "Ubuntu-24.04\r\nDebian\r\n"))
            .ToArray();

        var names = WslGenerationService.ParseDistributionNames(bytes);

        Assert.Collection(
            names,
            item => Assert.Equal("Debian", item),
            item => Assert.Equal("Ubuntu-24.04", item));
    }
}
