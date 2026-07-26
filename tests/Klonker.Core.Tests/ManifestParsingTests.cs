using Klonker.Core.Registry;
using Klonker.Core.Templates;
using Klonker.Core.Tests.TestSupport;

namespace Klonker.Core.Tests;

public sealed class ManifestParsingTests
{
    [Fact]
    public void Load_ValidManifest_ReturnsTypedManifest()
    {
        using var package = new TestPackage(TestManifests.Valid);

        var result = TemplatePackageLoader.Load(package.RootPath);

        Assert.True(result.IsSuccess);
        Assert.Equal("test.console.windows", result.Value!.Manifest.Id);
        Assert.Equal(2, result.Value.Manifest.Parameters.Length);
        Assert.Equal(TemplateParameterType.Choice, result.Value.Manifest.Parameters[1].Type);
    }

    [Fact]
    public void Load_MissingRequiredManifestProperty_ReturnsError()
    {
        var manifest = TestManifests.Valid.Replace(
            "description = \"A test package.\"",
            string.Empty,
            StringComparison.Ordinal);
        using var package = new TestPackage(manifest);

        var result = TemplatePackageLoader.Load(package.RootPath);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "manifest.property_required" &&
                     issue.Message.Contains("description", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_UnsupportedSchemaVersion_ReturnsError()
    {
        var manifest = TestManifests.Valid.Replace(
            "schema_version = 0",
            "schema_version = 7",
            StringComparison.Ordinal);
        using var package = new TestPackage(manifest);

        var result = TemplatePackageLoader.Load(package.RootPath);

        Assert.Contains(result.Issues, issue => issue.Code == "manifest.schema_unsupported");
    }

    [Fact]
    public void Load_DuplicateParameterIds_ReturnsError()
    {
        var manifest = TestManifests.Valid + """

            [[parameters]]
            id = "project_name"
            type = "string"
            label = "Another project name"
            required = false
            """;
        using var package = new TestPackage(manifest);

        var result = TemplatePackageLoader.Load(package.RootPath);

        Assert.Contains(result.Issues, issue => issue.Code == "parameter.id_duplicate");
    }

    [Fact]
    public void Load_ChoiceDefaultOutsideAllowedValues_ReturnsError()
    {
        var manifest = TestManifests.Valid.Replace(
            "default = \"23\"",
            "default = \"17\"",
            StringComparison.Ordinal);
        using var package = new TestPackage(manifest);

        var result = TemplatePackageLoader.Load(package.RootPath);

        Assert.Contains(
            result.Issues,
            issue => issue.Code == "parameter.choice_default_invalid");
    }

    [Fact]
    public void Load_OptionalPresentationMetadata_ReturnsLogoTagsAndFavorite()
    {
        var manifest = TestManifests.Valid.Replace(
            "source_license = \"MIT\"",
            """
            source_license = "MIT"
            logo = "template-logo.png"
            tags = ["graphics", "gamedev", "gof2", "modding"]
            favorite = true
            """,
            StringComparison.Ordinal);
        using var package = new TestPackage(manifest);
        File.WriteAllBytes(
            Path.Combine(package.RootPath, "template-logo.png"),
            [0x89, 0x50, 0x4E, 0x47]);

        var result = TemplatePackageLoader.Load(package.RootPath);

        Assert.True(result.IsSuccess);
        Assert.Equal("template-logo.png", result.Value!.Manifest.Logo);
        Assert.EndsWith("template-logo.png", result.Value.LogoPath);
        Assert.Equal(
            ["graphics", "gamedev", "gof2", "modding"],
            result.Value.Manifest.Tags.ToArray());
        Assert.True(result.Value.Manifest.IsFavorite);
    }

    [Fact]
    public void Load_LogoTraversal_ReturnsPathError()
    {
        var manifest = TestManifests.Valid.Replace(
            "source_license = \"MIT\"",
            """
            source_license = "MIT"
            logo = "../outside.png"
            """,
            StringComparison.Ordinal);
        using var package = new TestPackage(manifest);

        var result = TemplatePackageLoader.Load(package.RootPath);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "path.traversal");
    }

    [Fact]
    public void Load_DuplicateTagsIgnoringCase_ReturnsError()
    {
        var manifest = TestManifests.Valid.Replace(
            "source_license = \"MIT\"",
            """
            source_license = "MIT"
            tags = ["GameDev", "gamedev"]
            """,
            StringComparison.Ordinal);
        using var package = new TestPackage(manifest);

        var result = TemplatePackageLoader.Load(package.RootPath);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "manifest.tag_duplicate");
    }

    [Fact]
    public void Load_Prerequisites_ReturnsDisplayOnlyRequirements()
    {
        var prerequisiteText = """
            [[prerequisites]]
            id = "cmake"
            name = "CMake 3.20+"
            description = "Required after generation."
            required_for = "build"

            """;
        var manifest = TestManifests.Valid.Insert(
            TestManifests.Valid.IndexOf("[[parameters]]", StringComparison.Ordinal),
            prerequisiteText);
        using var package = new TestPackage(manifest);

        var result = TemplatePackageLoader.Load(package.RootPath);

        Assert.True(result.IsSuccess);
        var parsedPrerequisite = Assert.Single(result.Value!.Manifest.Prerequisites);
        Assert.Equal("cmake", parsedPrerequisite.Id);
        Assert.Equal("build", parsedPrerequisite.RequiredFor);
    }

    [Fact]
    public void Load_DuplicatePrerequisiteIds_ReturnsError()
    {
        const string prerequisite = """
            [[prerequisites]]
            id = "cmake"
            name = "CMake"
            description = "Required after generation."
            required_for = "build"

            """;
        var manifest = TestManifests.Valid.Insert(
            TestManifests.Valid.IndexOf("[[parameters]]", StringComparison.Ordinal),
            prerequisite + prerequisite);
        using var package = new TestPackage(manifest);

        var result = TemplatePackageLoader.Load(package.RootPath);

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "prerequisite.id_duplicate");
    }

    [Fact]
    public void Load_SampleRegistry_ReturnsSampleIdentity()
    {
        var result = LocalRegistryLoader.Load(RepositoryPaths.SampleRegistry);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(result.Value!.Templates);
        Assert.Equal("std.cpp-cli.windows-cmake", entry.TemplateId);
        Assert.Equal(RegistryIndexLoader.SupportedSchemaVersion, result.Value.SchemaVersion);
        Assert.Equal(64, entry.PackageSha256.Length);
        Assert.True(entry.PackageSizeBytes > 0);
        Assert.True(
            LocalRegistryLoader.ResolvePackagePath(result.Value, entry).IsSuccess);
    }
}
