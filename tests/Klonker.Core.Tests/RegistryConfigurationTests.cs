using Klonker.Core.Registry;
using Klonker.Core.Tests.TestSupport;
using Klonker.Desktop.Services;

namespace Klonker.Core.Tests;

public sealed class RegistryConfigurationTests
{
    [Fact]
    public void OfficialRegistryUrl_UsesCanonicalRawGitHubIndex()
    {
        Assert.Equal(
            "https://raw.githubusercontent.com/klonkerdev/registry/main/dist/registry.json",
            RegistryConfigurationStore.OfficialRegistryUrl);
    }

    [Fact]
    public void Load_ExplicitDevelopmentSeedCreatesDevelopmentAndOfficialSources()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new RegistryConfigurationStore(
            temporaryDirectory.Path,
            RepositoryPaths.SampleRegistry,
            "https://registry.example/registry.json");

        var result = store.Load();

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(result.Value!.ConfigurationPath));
        Assert.StartsWith(temporaryDirectory.Path, result.Value.ConfigurationPath);
        Assert.StartsWith(temporaryDirectory.Path, result.Value.CacheRoot);
        Assert.Equal(2, result.Value.Sources.Length);
        Assert.Contains(
            result.Value.Sources,
            source => source.Kind == RegistrySourceKind.Local &&
                      source.Location == RepositoryPaths.SampleRegistry);
        Assert.Contains(
            result.Value.Sources,
            source => source.Kind == RegistrySourceKind.Remote &&
                      source.Location == "https://registry.example/registry.json" &&
                      source.TrustPolicy?.RequireSignature == true &&
                      source.TrustPolicy.PublisherId ==
                          RegistryConfigurationStore.OfficialPublisherId &&
                      source.TrustPolicy.Keys.Any(key =>
                          key.KeyId ==
                              RegistryConfigurationStore.OfficialSigningKeyId));
    }

    [Fact]
    public void Load_NormalStartupSeedsOnlyOfficialSource()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new RegistryConfigurationStore(
            temporaryDirectory.Path,
            RepositoryPaths.SampleRegistry,
            "https://registry.example/registry.json",
            seedDevelopmentRegistry: false);

        var result = store.Load();

        Assert.True(result.IsSuccess);
        var source = Assert.Single(result.Value!.Sources);
        Assert.Equal(RegistrySourceKind.Remote, source.Kind);
        Assert.Equal("https://registry.example/registry.json", source.Location);
    }

    [Fact]
    public void Load_NormalStartupRemovesExactLegacyDevelopmentSample()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var legacyStore = new RegistryConfigurationStore(
            temporaryDirectory.Path,
            RepositoryPaths.SampleRegistry,
            "https://registry.example/registry.json");
        Assert.True(legacyStore.Load().IsSuccess);

        var currentStore = new RegistryConfigurationStore(
            temporaryDirectory.Path,
            RepositoryPaths.SampleRegistry,
            "https://registry.example/registry.json",
            seedDevelopmentRegistry: false);
        var result = currentStore.Load();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Sources);
        Assert.DoesNotContain(
            result.Value.Sources,
            source => source.Kind == RegistrySourceKind.Local);
        Assert.Contains(
            result.Issues,
            issue => issue.Code ==
                "registry.legacy_development_source_removed");
        Assert.DoesNotContain(
            "Development samples",
            File.ReadAllText(result.Value.ConfigurationPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_NormalStartupPreservesExplicitlyRenamedDevelopmentRegistry()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var legacyStore = new RegistryConfigurationStore(
            temporaryDirectory.Path,
            RepositoryPaths.SampleRegistry,
            "https://registry.example/registry.json");
        var legacy = legacyStore.Load();
        Assert.True(legacy.IsSuccess);
        var renamedSources = legacy.Value!.Sources
            .Select(source => source.Kind == RegistrySourceKind.Local
                ? source with { Name = "My local test registry" }
                : source);
        Assert.True(legacyStore.Save(
            offline: false,
            sources: renamedSources).IsSuccess);

        var currentStore = new RegistryConfigurationStore(
            temporaryDirectory.Path,
            RepositoryPaths.SampleRegistry,
            "https://registry.example/registry.json",
            seedDevelopmentRegistry: false);
        var result = currentStore.Load();

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Sources.Length);
        Assert.Contains(
            result.Value.Sources,
            source => source.Name == "My local test registry");
        Assert.DoesNotContain(
            result.Issues,
            issue => issue.Code ==
                "registry.legacy_development_source_removed");
    }

    [Fact]
    public void Load_RelativeLocalLocationResolvesBesideConfiguration()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var registryDirectory = Path.Combine(temporaryDirectory.Path, "catalog");
        Directory.CreateDirectory(registryDirectory);
        var configuration = """
            {
              "schema_version": 0,
              "offline": true,
              "sources": [
                {
                  "name": "Personal templates",
                  "kind": "local",
                  "location": "catalog/registry.json",
                  "enabled": true
                }
              ]
            }
            """;
        File.WriteAllText(
            Path.Combine(temporaryDirectory.Path, "registries.json"),
            configuration);
        var store = new RegistryConfigurationStore(temporaryDirectory.Path);

        var result = store.Load();

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Offline);
        var source = Assert.Single(result.Value.Sources);
        Assert.Equal(
            Path.Combine(registryDirectory, "registry.json"),
            source.Location);
    }

    [Fact]
    public void Load_UnknownSourceKindReturnsReadableError()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(temporaryDirectory.Path, "registries.json"),
            """
            {
              "schema_version": 0,
              "offline": false,
              "sources": [
                {
                  "name": "Broken",
                  "kind": "database",
                  "location": "anything",
                  "enabled": true
                }
              ]
            }
            """);
        var store = new RegistryConfigurationStore(temporaryDirectory.Path);

        var result = store.Load();

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "registry.configuration_kind_invalid");
    }

    [Fact]
    public void Load_NullSourceArrayReturnsReadableError()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        File.WriteAllText(
            Path.Combine(temporaryDirectory.Path, "registries.json"),
            """
            {
              "schema_version": 0,
              "offline": false,
              "sources": null
            }
            """);
        var store = new RegistryConfigurationStore(temporaryDirectory.Path);

        var result = store.Load();

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "registry.configuration_sources_required");
    }
}
