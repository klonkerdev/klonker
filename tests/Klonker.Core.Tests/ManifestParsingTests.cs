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
    public void Load_SampleRegistry_ReturnsSampleIdentity()
    {
        var result = LocalRegistryLoader.Load(RepositoryPaths.SampleRegistry);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(result.Value!.Templates);
        Assert.Equal("official.cpp-cli.windows-cmake", entry.TemplateId);
        Assert.True(
            LocalRegistryLoader.ResolvePackagePath(result.Value, entry).IsSuccess);
    }
}
