using System.Collections.Immutable;
using System.Security.Cryptography;
using Klonker.Core.Registry;
using Klonker.Core.Tests.TestSupport;
using Klonker.Desktop.Services;

namespace Klonker.Core.Tests;

public sealed class LocalSettingsTests
{
    [Fact]
    public void Favorites_AreStoredByRegistryAndTemplateOutsidePackages()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new FavoriteStore(temporaryDirectory.Path);
        const string identity =
            "klonker.official:std.cpp-cli.windows-cmake";

        var saved = store.SetFavorite(identity, isFavorite: true);
        var loaded = store.Load();

        Assert.True(saved.IsSuccess);
        Assert.True(loaded.IsSuccess);
        Assert.True(loaded.Value!.Contains(identity));
        Assert.Equal(
            ["favorites.json"],
            Directory.GetFiles(
                    temporaryDirectory.Path,
                    "*",
                    SearchOption.AllDirectories)
                .Select(Path.GetFileName));
    }

    [Fact]
    public void ApplicationSettings_RoundTripAppearanceDiagnosticsAndProbeConsent()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new AppSettingsStore(temporaryDirectory.Path);
        var saved = store.Save(new AppSettingsSnapshot(
            store.StoragePath,
            AppAppearance.Light,
            DiagnosticLoggingEnabled: true,
            DiagnosticLogLevel.Verbose,
            PrerequisiteProbesEnabled: true,
            RegistryDownloadTimeoutSeconds: 45,
            RegistryVersionPreference.LatestIncludingPrerelease,
            ImmutableDictionary<string, string>.Empty.Add(
                "tests:item",
                "2.0.0-beta.1"),
            RegistryDuplicateSourcePolicy.RejectDuplicates));

        var loaded = store.Load();

        Assert.True(saved.IsSuccess);
        Assert.Equal(AppAppearance.Light, loaded.Value?.Appearance);
        Assert.True(loaded.Value?.DiagnosticLoggingEnabled);
        Assert.Equal(DiagnosticLogLevel.Verbose, loaded.Value?.DiagnosticLogLevel);
        Assert.True(loaded.Value?.PrerequisiteProbesEnabled);
        Assert.Equal(45, loaded.Value?.RegistryDownloadTimeoutSeconds);
        Assert.Equal(
            RegistryVersionPreference.LatestIncludingPrerelease,
            loaded.Value?.RegistryVersionPreference);
        Assert.Equal(
            "2.0.0-beta.1",
            loaded.Value?.RegistryVersionPins?["tests:item"]);
        Assert.Equal(
            RegistryDuplicateSourcePolicy.RejectDuplicates,
            loaded.Value?.RegistryDuplicateSourcePolicy);
    }

    [Fact]
    public void PersonalCatalogTabs_AreAppLocalAndRoundTripMixedPolicies()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new CatalogTabStore(temporaryDirectory.Path);
        var saved = store.Save(
        [
            new CatalogTabDefinition(
                "work",
                "Work",
                CatalogTabKind.SelectedTemplates,
                ImmutableArray.Create("tests:template")),
            new CatalogTabDefinition(
                "modules",
                "Modules",
                CatalogTabKind.FavoriteModules,
                []),
        ]);

        var loaded = store.Load();

        Assert.True(saved.IsSuccess);
        Assert.True(loaded.IsSuccess);
        Assert.Equal(2, loaded.Value!.Tabs.Length);
        Assert.Contains(
            loaded.Value.Tabs,
            tab => tab.Kind == CatalogTabKind.FavoriteModules);
        Assert.Equal(
            ["catalog-tabs.json"],
            Directory.GetFiles(
                    temporaryDirectory.Path,
                    "*",
                    SearchOption.AllDirectories)
                .Select(Path.GetFileName));
    }

    [Fact]
    public void RegistryConfiguration_RoundTripsActiveAndRevokedPublisherKeys()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var activeRsa = RSA.Create(2048);
        using var revokedRsa = RSA.Create(2048);
        var store = new RegistryConfigurationStore(temporaryDirectory.Path);
        var policy = new RegistryTrustPolicy(
            "tests.publisher",
            ImmutableArray.Create(
                new RegistryTrustedKey(
                    "2026-active",
                    RegistrySignatureVerifier.RsaPkcs1Sha256,
                    Convert.ToBase64String(
                        activeRsa.ExportSubjectPublicKeyInfo())),
                new RegistryTrustedKey(
                    "2025-revoked",
                    RegistrySignatureVerifier.RsaPkcs1Sha256,
                    Convert.ToBase64String(
                        revokedRsa.ExportSubjectPublicKeyInfo()),
                    Revoked: true)));

        var saved = store.Save(
            offline: false,
            [
                new RegistrySource(
                    "Signed registry",
                    RegistrySourceKind.Remote,
                    "https://registry.example/registry.json",
                    TrustPolicy: policy),
            ]);

        Assert.True(
            saved.IsSuccess,
            string.Join(Environment.NewLine, saved.Issues.Select(issue => issue.Message)));
        var source = Assert.Single(saved.Value!.Sources);
        Assert.True(source.TrustPolicy?.RequireSignature);
        Assert.Equal("tests.publisher", source.TrustPolicy?.PublisherId);
        Assert.Equal(2, source.TrustPolicy?.Keys.Length);
        Assert.Single(source.TrustPolicy!.Keys, key => key.Revoked);
    }
}
