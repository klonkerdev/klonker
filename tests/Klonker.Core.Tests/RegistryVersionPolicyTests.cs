using System.Collections.Immutable;
using Klonker.Core.Registry;
using Klonker.Core.Templates;

namespace Klonker.Core.Tests;

public sealed class RegistryVersionPolicyTests
{
    [Fact]
    public void LatestStablePrefersStableOverNewerPrerelease()
    {
        var stable = Package("1.9.0");
        var prerelease = Package("2.0.0-beta.1");

        var result = RegistryVersionSelector.Select(
            [prerelease, stable],
            RegistryVersionPreference.LatestStable);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "1.9.0",
            Assert.Single(result.Value!.Selections).Selected.Entry.Version);
    }

    [Fact]
    public void ExactPinOverridesDefaultAndUnavailablePinWarns()
    {
        var first = Package("1.0.0");
        var second = Package("2.0.0");
        var pinned = RegistryVersionSelector.Select(
            [first, second],
            RegistryVersionPreference.LatestStable,
            new Dictionary<string, string>
            {
                ["tests:tests.example.windows"] = "1.0.0",
            });
        var missing = RegistryVersionSelector.Select(
            [first, second],
            RegistryVersionPreference.LatestStable,
            new Dictionary<string, string>
            {
                ["tests:tests.example.windows"] = "9.0.0",
            });

        Assert.Equal(
            "1.0.0",
            Assert.Single(pinned.Value!.Selections).Selected.Entry.Version);
        Assert.Equal(
            "2.0.0",
            Assert.Single(missing.Value!.Selections).Selected.Entry.Version);
        Assert.Contains(
            missing.Issues,
            issue => issue.Code == "registry.version_pin_unavailable");
    }

    [Fact]
    public void IndexAllowsSameQualifiedTemplateAcrossDistinctVersions()
    {
        var checksum = new string('a', 64);
        var json = $$"""
            {
              "schema_version": 1,
              "registry_id": "tests",
              "display_name": "Tests",
              "templates": [
                {
                  "family_id": "tests.example",
                  "variant_id": "windows",
                  "template_id": "tests.example.windows",
                  "name": "Example",
                  "description": "Version one",
                  "version": "1.0.0",
                  "target_os": "windows",
                  "build_system": "none",
                  "language": "cpp",
                  "package_path": "packages/one",
                  "license_summary": "MIT",
                  "package_sha256": "{{checksum}}",
                  "package_size_bytes": 1
                },
                {
                  "family_id": "tests.example",
                  "variant_id": "windows",
                  "template_id": "tests.example.windows",
                  "name": "Example",
                  "description": "Version two",
                  "version": "2.0.0",
                  "target_os": "windows",
                  "build_system": "none",
                  "language": "cpp",
                  "package_path": "packages/two",
                  "license_summary": "MIT",
                  "package_sha256": "{{checksum}}",
                  "package_size_bytes": 1
                }
              ],
              "modules": []
            }
            """;

        var result = RegistryIndexLoader.Parse(json, "test");

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Templates.Length);
    }

    private static RegistryTemplatePackage Package(string version)
    {
        var manifest = new TemplateManifest(
            0,
            "tests.example.windows",
            "tests.example",
            "windows",
            "Example",
            "Example",
            version,
            "windows",
            "none",
            "MIT",
            ImmutableArray<TemplateParameterDefinition>.Empty);
        var package = new TemplatePackage(
            ".",
            ".",
            manifest,
            ImmutableArray<TemplateSourceFile>.Empty,
            RegistryId: "tests");
        var entry = new RegistryTemplateEntry(
            manifest.FamilyId,
            manifest.VariantId,
            manifest.Id,
            manifest.Name,
            manifest.Description,
            version,
            manifest.TargetOs,
            manifest.BuildSystem,
            $"packages/{version}",
            manifest.SourceLicense,
            new string('a', 64),
            1,
            manifest.Language);
        return new RegistryTemplatePackage(
            "tests",
            "Tests",
            entry,
            package);
    }
}
