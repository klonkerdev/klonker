using Klonker.Core.Generation;
using Klonker.Core.Templates;
using Klonker.Core.Tests.TestSupport;

namespace Klonker.Core.Tests;

public sealed class PlanningTests
{
    [Fact]
    public async Task CreatePlan_SampleCppTemplate_HasExpectedTreeAndContent()
    {
        var packageResult = TemplatePackageLoader.Load(RepositoryPaths.SamplePackage);
        Assert.True(packageResult.IsSuccess);

        var result = await TemplatePlanner.CreatePlanAsync(
            packageResult.Value!,
            new Dictionary<string, object?>
            {
                ["project_name"] = "DemoCli",
                ["cpp_standard"] = "20",
            });

        Assert.True(
            result.IsSuccess,
            string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
        Assert.Equal(
            [
                "CMakeLists.txt",
                "README.md",
                "src/cli/Arguments.cpp",
                "src/cli/Arguments.hpp",
                "src/main.cpp",
            ],
            result.Value!.Files.Select(file => file.RelativePath));

        var cmake = Find(result.Value, "CMakeLists.txt").TextContent!;
        Assert.Contains("project(DemoCli VERSION 0.1.0 LANGUAGES CXX)", cmake);
        Assert.Contains("target_compile_features(DemoCli PRIVATE cxx_std_20)", cmake);

        var main = Find(result.Value, "src/main.cpp").TextContent!;
        Assert.Contains("ApplicationName = \"DemoCli\"", main);
        Assert.Contains("--version", main);
    }

    [Fact]
    public async Task CreatePlan_RepeatedRuns_AreByteForByteDeterministic()
    {
        var package = TemplatePackageLoader.Load(RepositoryPaths.SamplePackage).Value!;
        var values = new Dictionary<string, object?>
        {
            ["project_name"] = "StableCli",
            ["cpp_standard"] = "23",
        };

        var first = await TemplatePlanner.CreatePlanAsync(package, values);
        var second = await TemplatePlanner.CreatePlanAsync(package, values);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(
            first.Value!.Files.Select(file => file.RelativePath),
            second.Value!.Files.Select(file => file.RelativePath));
        for (var index = 0; index < first.Value.Files.Length; index++)
        {
            Assert.True(first.Value.Files[index].Content.AsSpan().SequenceEqual(
                second.Value.Files[index].Content.AsSpan()));
        }
    }

    [Fact]
    public async Task CreatePlan_DoesNotWriteToPackageOrCreateOutput()
    {
        var package = TemplatePackageLoader.Load(RepositoryPaths.SamplePackage).Value!;
        var before = Directory.GetFileSystemEntries(
                package.RootPath,
                "*",
                SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var unexpectedOutput = System.IO.Path.Combine(package.RootPath, "generated-output");

        var result = await TemplatePlanner.CreatePlanAsync(package, null);

        var after = Directory.GetFileSystemEntries(
                package.RootPath,
                "*",
                SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(result.IsSuccess);
        Assert.Equal(before, after);
        Assert.False(Directory.Exists(unexpectedOutput));
    }

    private static PlannedFile Find(GenerationPlan plan, string path) =>
        Assert.Single(plan.Files, file => file.RelativePath == path);
}
