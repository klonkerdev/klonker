using System.Text;
using Klonker.Core.Generation;
using Klonker.Core.Modules;
using Klonker.Core.Tests.TestSupport;

namespace Klonker.Core.Tests;

public sealed class ModuleGenerationTests
{
    [Fact]
    public async Task Module_PlanRendersSlotsInstructionsAndLicenseReport()
    {
        using var package = CreatePackage();
        var loaded = ModulePackageLoader.Load(package.Path);

        var result = await ModulePlanner.CreatePlanAsync(
            loaded.Value!,
            new Dictionary<string, object?>
            {
                ["module_root"] = "src/math",
                ["class_name"] = "Vector",
            });

        Assert.True(
            result.IsSuccess,
            string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
        var file = Assert.Single(result.Value!.FilePlan.Files);
        Assert.Equal("src/math/vector.hpp", file.RelativePath);
        Assert.Contains("class Vector", file.TextContent);
        Assert.Equal("src/math", result.Value.Slots["module_root"]);
        Assert.Contains(
            "add_subdirectory(src/math)",
            result.Value.PostGenerationInstructions);
        Assert.Equal(
            "Generated module sources: MIT. Dependencies: fmt 11.0.0 (MIT)",
            result.Value.LicenseReport.Summary);
    }

    [Fact]
    public async Task Module_ExistingUnrelatedFilesArePreserved()
    {
        using var package = CreatePackage();
        using var destination = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(destination.Path, "existing.txt"),
            "keep");
        var plan = await ModulePlanner.CreatePlanAsync(
            ModulePackageLoader.Load(package.Path).Value!,
            new Dictionary<string, object?>
            {
                ["module_root"] = "src/math",
                ["class_name"] = "Vector",
            });

        var result = await ModuleGenerationExecutor.ExecuteAsync(
            plan.Value!,
            destination.Path);

        Assert.True(result.Succeeded);
        Assert.Equal(
            "keep",
            File.ReadAllText(Path.Combine(destination.Path, "existing.txt")));
        Assert.Contains(
            "class Vector",
            File.ReadAllText(
                Path.Combine(destination.Path, "src", "math", "vector.hpp")));
    }

    [Fact]
    public async Task Module_PreflightConflictAbortsBeforeAnyWrite()
    {
        using var package = CreatePackage();
        using var destination = new TemporaryDirectory();
        Directory.CreateDirectory(
            Path.Combine(destination.Path, "src", "math"));
        var conflicting = Path.Combine(
            destination.Path,
            "src",
            "math",
            "vector.hpp");
        File.WriteAllText(conflicting, "user content");
        var plan = await ModulePlanner.CreatePlanAsync(
            ModulePackageLoader.Load(package.Path).Value!,
            new Dictionary<string, object?>
            {
                ["module_root"] = "src/math",
                ["class_name"] = "Vector",
            });

        var result = await ModuleGenerationExecutor.ExecuteAsync(
            plan.Value!,
            destination.Path);

        Assert.Equal(GenerationStatus.Rejected, result.Status);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "module.file_conflict");
        Assert.Equal("user content", File.ReadAllText(conflicting));
        Assert.Single(
            Directory.EnumerateFiles(
                destination.Path,
                "*",
                SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Module_UnsafeSlotPathIsRejected()
    {
        using var package = CreatePackage();
        var result = await ModulePlanner.CreatePlanAsync(
            ModulePackageLoader.Load(package.Path).Value!,
            new Dictionary<string, object?>
            {
                ["module_root"] = "../outside",
                ["class_name"] = "Vector",
            });

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "module.slot_path_invalid");
    }

    private static TemporaryDirectory CreatePackage()
    {
        var temporary = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(
            temporary.Path,
            "content",
            "{{ module_root }}"));
        File.WriteAllText(
            Path.Combine(temporary.Path, "module.toml"),
            """
            schema_version = 0
            id = "test.cpp-module"
            name = "C++ module"
            description = "Adds one C++ header."
            version = "1.0.0"
            language = "cpp"
            source_license = "MIT"
            tags = ["cpp", "module"]
            post_generation_instructions = "add_subdirectory({{ module_root }})"

            [[slots]]
            id = "module_root"
            label = "Module folder"
            description = "Relative destination."
            required = true
            default = "src/example"

            [[parameters]]
            id = "class_name"
            type = "string"
            label = "Class name"
            description = "C++ class."
            required = true
            default = "Example"
            validation = "cpp_identifier"

            [[dependencies]]
            id = "fmt"
            name = "fmt"
            version = "11.0.0"
            license = "MIT"
            project_url = "https://github.com/fmtlib/fmt"
            """,
            new UTF8Encoding(false));
        File.WriteAllText(
            Path.Combine(
                temporary.Path,
                "content",
                "{{ module_root }}",
                "{{ snake_case class_name }}.hpp.sbn"),
            """
            #pragma once
            class {{ class_name }} {};
            """,
            new UTF8Encoding(false));
        return temporary;
    }
}
