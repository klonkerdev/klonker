using Klonker.Core.Generation;
using Klonker.Core.Paths;
using Klonker.Core.Tests.TestSupport;

namespace Klonker.Core.Tests;

public sealed class PathSafetyTests
{
    [Theory]
    [InlineData("../outside.txt", "path.traversal")]
    [InlineData("folder/../outside.txt", "path.traversal")]
    public void NormalizeRelative_ParentTraversal_IsRejected(string path, string code)
    {
        var result = SafePath.NormalizeRelative(path);

        Assert.Contains(result.Issues, issue => issue.Code == code);
    }

    [Theory]
    [InlineData("/rooted/file.txt")]
    [InlineData("\\rooted\\file.txt")]
    public void NormalizeRelative_RootedPath_IsRejected(string path)
    {
        var result = SafePath.NormalizeRelative(path);

        Assert.Contains(result.Issues, issue => issue.Code == "path.rooted");
    }

    [Fact]
    public void NormalizeRelative_DriveQualifiedPath_IsRejected()
    {
        var result = SafePath.NormalizeRelative(@"C:folder\file.txt");

        Assert.Contains(result.Issues, issue => issue.Code == "path.drive_qualified");
    }

    [Fact]
    public void NormalizeRelative_UncPath_IsRejected()
    {
        var result = SafePath.NormalizeRelative(@"\\server\share\file.txt");

        Assert.Contains(result.Issues, issue => issue.Code == "path.unc");
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("src/aux.txt")]
    [InlineData("LPT9.log")]
    public void NormalizeRelative_ReservedWindowsName_IsRejected(string path)
    {
        var result = SafePath.NormalizeRelative(path);

        Assert.Contains(result.Issues, issue => issue.Code == "path.reserved_name");
    }

    [Fact]
    public void NormalizeRelative_SafeNestedPath_IsCanonicalized()
    {
        var result = SafePath.NormalizeRelative(@"src\cli\Arguments.cpp");

        Assert.True(result.IsSuccess);
        Assert.Equal("src/cli/Arguments.cpp", result.Value);
    }

    [Fact]
    public async Task Plan_DestinationsDifferingOnlyByCase_AreRejected()
    {
        using var package = new TestPackage(
            TwoStringParametersManifest,
            new Dictionary<string, byte[]>
            {
                ["{{ first }}.txt.sbn"] = TestPackage.Text("first"),
                ["{{ second }}.txt.sbn"] = TestPackage.Text("second"),
            });

        var result = await TemplatePlanner.CreatePlanAsync(
            package.Load(),
            new Dictionary<string, object?>
            {
                ["first"] = "Readme",
                ["second"] = "README",
            });

        Assert.Contains(
            result.Issues,
            issue => issue.Code == "plan.duplicate_destination");
    }

    [Fact]
    public async Task Plan_FileDirectoryCollision_IsRejected()
    {
        using var package = new TestPackage(
            TwoStringParametersManifest,
            new Dictionary<string, byte[]>
            {
                ["{{ first }}.sbn"] = TestPackage.Text("file"),
                ["src/main.txt.sbn"] = TestPackage.Text("nested"),
            });

        var result = await TemplatePlanner.CreatePlanAsync(
            package.Load(),
            new Dictionary<string, object?> { ["first"] = "src" });

        Assert.Contains(
            result.Issues,
            issue => issue.Code == "plan.file_directory_collision");
    }

    [Fact]
    public async Task Plan_ParameterCannotInjectPathSeparator()
    {
        using var package = new TestPackage(
            TwoStringParametersManifest,
            new Dictionary<string, byte[]>
            {
                ["{{ first }}.txt.sbn"] = TestPackage.Text("content"),
            });

        var result = await TemplatePlanner.CreatePlanAsync(
            package.Load(),
            new Dictionary<string, object?> { ["first"] = "../outside" });

        Assert.Contains(
            result.Issues,
            issue => issue.Code == "path.injected_separator");
    }

    private const string TwoStringParametersManifest = """
        schema_version = 0
        id = "test.paths"
        family_id = "test"
        variant_id = "paths"
        name = "Paths"
        description = "Path tests."
        version = "1.0.0"
        target_os = "windows"
        build_system = "none"
        source_license = "MIT"

        [[parameters]]
        id = "first"
        type = "string"
        label = "First"
        required = true

        [[parameters]]
        id = "second"
        type = "string"
        label = "Second"
        required = false
        """;
}
