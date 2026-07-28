using System.Collections.Immutable;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Klonker.Core.Registry;
using Klonker.Core.Templates;
using Klonker.Core.Tests.TestSupport;

namespace Klonker.Core.Tests;

public sealed class RegistryCatalogTests
{
    [Fact]
    public void Index_ValidVersionOne_ReturnsChecksumMetadata()
    {
        var result = RegistryIndexLoader.Parse(
            CreateIndexJson(new string('a', 64), packageSize: 42),
            "test registry");

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(result.Value!.Templates);
        Assert.Equal(new string('a', 64), entry.PackageSha256);
        Assert.Equal(42, entry.PackageSizeBytes);
        Assert.Equal("cpp", entry.Language);
    }

    [Fact]
    public void Index_LegacyEntryWithoutLanguage_RemainsCompatible()
    {
        var json = CreateIndexJson(new string('a', 64), packageSize: 42)
            .Replace(
                """
                      "language": "cpp",
                """,
                string.Empty,
                StringComparison.Ordinal);

        var result = RegistryIndexLoader.Parse(json, "legacy test registry");

        Assert.True(result.IsSuccess);
        Assert.Equal("unknown", Assert.Single(result.Value!.Templates).Language);
    }

    [Fact]
    public void Index_MissingChecksum_IsRejected()
    {
        var json = CreateIndexJson(new string('a', 64), packageSize: 42)
            .Replace(
                $"""
                     "package_sha256": "{new string('a', 64)}",
                """,
                string.Empty,
                StringComparison.Ordinal);

        var result = RegistryIndexLoader.Parse(json, "test registry");

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "registry.property_required" &&
                     issue.Message.Contains("package_sha256", StringComparison.Ordinal));
    }

    [Fact]
    public void Index_NullTemplateArray_IsRejectedWithoutThrowing()
    {
        var json = """
            {
              "schema_version": 1,
              "registry_id": "tests.remote",
              "display_name": "Test templates",
              "templates": null
            }
            """;

        var result = RegistryIndexLoader.Parse(json, "test registry");

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "registry.templates_required");
    }

    [Fact]
    public async Task PackageIntegrity_DirectoryDigestDetectsChanges()
    {
        using var package = new TestPackage(
            TestManifests.Valid,
            new Dictionary<string, byte[]>
            {
                ["main.cpp"] = TestPackage.Text("first"),
            });
        var first = await PackageIntegrity.InspectAsync(package.RootPath);

        File.WriteAllText(
            Path.Combine(package.RootPath, "content", "main.cpp"),
            "second");
        var second = await PackageIntegrity.InspectAsync(package.RootPath);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value!.Sha256, second.Value!.Sha256);
        Assert.NotEqual(first.Value.SizeBytes, second.Value.SizeBytes);
    }

    [Fact]
    public async Task Archive_TraversalEntry_IsRejectedWithoutWrites()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var archivePath = Path.Combine(temporaryDirectory.Path, "unsafe.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../outside.txt");
            await using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            await writer.WriteAsync("escaped");
        }

        var destination = Path.Combine(temporaryDirectory.Path, "package");
        var result = await PackageArchiveExtractor.ExtractAsync(
            archivePath,
            destination);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Issues, issue => issue.Code == "path.traversal");
        Assert.False(Directory.Exists(destination));
        Assert.False(File.Exists(Path.Combine(temporaryDirectory.Path, "outside.txt")));
    }

    [Fact]
    public async Task Archive_CaseInsensitiveDuplicate_IsRejected()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var archivePath = Path.Combine(temporaryDirectory.Path, "duplicate.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            await WriteEntryAsync(archive, "README.md", "first");
            await WriteEntryAsync(archive, "readme.md", "second");
        }

        var result = await PackageArchiveExtractor.ExtractAsync(
            archivePath,
            Path.Combine(temporaryDirectory.Path, "package"));

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "registry.package_duplicate_path");
    }

    [Fact]
    public async Task LocalRegistry_VerifiesSampleAndQualifiesIdentity()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var httpClient = new HttpClient(new RejectingHttpHandler());
        var service = new RegistryCatalogService(httpClient);

        var result = await service.LoadAsync(
            [
                new RegistrySource(
                    "Development samples",
                    RegistrySourceKind.Local,
                    RepositoryPaths.SampleRegistry),
            ],
            new RegistryCatalogOptions(
                Path.Combine(temporaryDirectory.Path, "cache")));

        Assert.True(
            result.IsSuccess,
            string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)));
        var sampleManifest = TemplatePackageLoader.Load(
            RepositoryPaths.SamplePackage).Value!.Manifest;
        var template = Assert.Single(
            result.Value!.Templates,
            item => item.Entry.TemplateId == sampleManifest.Id);
        Assert.Equal("klonker.samples.local", template.RegistryId);
        Assert.Equal(
            $"klonker.samples.local:{sampleManifest.Id}@{sampleManifest.Version}",
            template.QualifiedId);
        Assert.Equal(template.RegistryId, template.Package.RegistryId);
        var module = Assert.Single(result.Value.Modules);
        Assert.Equal("std.cpp-cmake-submodule", module.Entry.ModuleId);
        Assert.Equal(template.RegistryId, module.Package.RegistryId);
    }

    [Fact]
    public async Task RemoteRegistry_DownloadsVerifiesCachesAndWorksOffline()
    {
        using var fixture = new RemoteRegistryFixture();
        using var onlineClient = new HttpClient(
            new MappedHttpHandler(fixture.Responses));
        var onlineService = new RegistryCatalogService(onlineClient);

        var online = await onlineService.LoadAsync(
            [fixture.Source],
            new RegistryCatalogOptions(fixture.CacheRoot));

        Assert.True(
            online.IsSuccess,
            string.Join(Environment.NewLine, online.Issues.Select(issue => issue.Message)));
        Assert.Single(online.Value!.Templates);
        Assert.Contains(
            online.Issues,
            issue => issue.Code == "registry.package_cached");

        var rejectingHandler = new RejectingHttpHandler();
        using var offlineClient = new HttpClient(rejectingHandler);
        var offlineService = new RegistryCatalogService(offlineClient);
        var offline = await offlineService.LoadAsync(
            [fixture.Source],
            new RegistryCatalogOptions(fixture.CacheRoot, Offline: true));

        Assert.True(offline.IsSuccess);
        Assert.Single(offline.Value!.Templates);
        Assert.Equal(0, rejectingHandler.RequestCount);
        Assert.Contains(
            offline.Issues,
            issue => issue.Code == "registry.offline_cache");
    }

    [Fact]
    public async Task RemoteRegistry_NetworkFailureFallsBackToValidatedIndexCache()
    {
        using var fixture = new RemoteRegistryFixture();
        using (var onlineClient = new HttpClient(
                   new MappedHttpHandler(fixture.Responses)))
        {
            var service = new RegistryCatalogService(onlineClient);
            var seeded = await service.LoadAsync(
                [fixture.Source],
                new RegistryCatalogOptions(fixture.CacheRoot));
            Assert.True(seeded.IsSuccess);
        }

        using var unavailableClient = new HttpClient(new RejectingHttpHandler());
        var fallbackService = new RegistryCatalogService(unavailableClient);
        var fallback = await fallbackService.LoadAsync(
            [fixture.Source],
            new RegistryCatalogOptions(fixture.CacheRoot));

        Assert.True(fallback.IsSuccess);
        Assert.Contains(
            fallback.Issues,
            issue => issue.Code == "registry.index_cache_fallback");
    }

    [Fact]
    public async Task RemoteRegistry_RequiredPublisherSignatureIsVerifiedAndCached()
    {
        using var fixture = new RemoteRegistryFixture(signed: true);
        using var onlineClient = new HttpClient(
            new MappedHttpHandler(fixture.Responses));
        var service = new RegistryCatalogService(onlineClient);

        var online = await service.LoadAsync(
            [fixture.Source],
            new RegistryCatalogOptions(fixture.CacheRoot));

        Assert.True(
            online.IsSuccess,
            string.Join(Environment.NewLine, online.Issues.Select(issue => issue.Message)));
        Assert.Contains(
            online.Issues,
            issue => issue.Code == "registry.signature_verified");

        using var offlineClient = new HttpClient(new RejectingHttpHandler());
        var offlineService = new RegistryCatalogService(offlineClient);
        var offline = await offlineService.LoadAsync(
            [fixture.Source],
            new RegistryCatalogOptions(fixture.CacheRoot, Offline: true));

        Assert.True(offline.IsSuccess);
        Assert.Contains(
            offline.Issues,
            issue => issue.Code == "registry.signature_verified");
    }

    [Fact]
    public async Task RemoteRegistry_RequiredPublisherSignatureCannotBeOmitted()
    {
        using var fixture = new RemoteRegistryFixture(signed: true);
        var unsignedResponses = fixture.Responses
            .Where(item => !item.Key.EndsWith(
                ".sig.json",
                StringComparison.Ordinal))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        using var client = new HttpClient(new MappedHttpHandler(unsignedResponses));
        var service = new RegistryCatalogService(client);

        var result = await service.LoadAsync(
            [fixture.Source],
            new RegistryCatalogOptions(fixture.CacheRoot));

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "registry.signature_http_status");
    }

    [Fact]
    public void RegistrySignature_TamperingAndRevokedKeysAreRejected()
    {
        using var rsa = RSA.Create(2048);
        var indexBytes = Encoding.UTF8.GetBytes("""{"schema_version":1}""");
        var key = new RegistryTrustedKey(
            "test-2026",
            RegistrySignatureVerifier.RsaPkcs1Sha256,
            Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()));
        var signature = CreateSignatureJson(
            indexBytes,
            rsa,
            "tests.publisher",
            key.KeyId);
        var policy = new RegistryTrustPolicy(
            "tests.publisher",
            ImmutableArray.Create(key));

        var tampered = RegistrySignatureVerifier.Verify(
            Encoding.UTF8.GetBytes("""{"schema_version":2}"""),
            signature,
            policy,
            "test signature");
        var revoked = RegistrySignatureVerifier.Verify(
            indexBytes,
            signature,
            policy with
            {
                Keys = ImmutableArray.Create(key with { Revoked = true }),
            },
            "test signature");

        Assert.False(tampered.IsSuccess);
        Assert.Contains(
            tampered.Issues,
            issue => issue.Code == "registry.signature_hash_mismatch");
        Assert.False(revoked.IsSuccess);
        Assert.Contains(
            revoked.Issues,
            issue => issue.Code == "registry.publisher_key_revoked");
    }

    [Fact]
    public async Task RemoteRegistry_ChecksumMismatchRejectsPackage()
    {
        using var fixture = new RemoteRegistryFixture(checksumOverride: new string('f', 64));
        using var httpClient = new HttpClient(
            new MappedHttpHandler(fixture.Responses));
        var service = new RegistryCatalogService(httpClient);

        var result = await service.LoadAsync(
            [fixture.Source],
            new RegistryCatalogOptions(fixture.CacheRoot));

        Assert.False(result.IsSuccess);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "registry.package_checksum_mismatch");
    }

    [Fact]
    public async Task RemoteRegistry_OfflineWithoutCacheDoesNotUseNetwork()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var handler = new RejectingHttpHandler();
        using var httpClient = new HttpClient(handler);
        var service = new RegistryCatalogService(httpClient);

        var result = await service.LoadAsync(
            [
                new RegistrySource(
                    "Remote tests",
                    RegistrySourceKind.Remote,
                    "https://registry.example/registry.json"),
            ],
            new RegistryCatalogOptions(
                Path.Combine(temporaryDirectory.Path, "cache"),
                Offline: true));

        Assert.False(result.IsSuccess);
        Assert.Equal(0, handler.RequestCount);
        Assert.Contains(
            result.Issues,
            issue => issue.Code == "registry.offline_index_missing");
    }

    private static string CreateIndexJson(string checksum, long packageSize) =>
        $$"""
        {
          "schema_version": 1,
          "registry_id": "tests.remote",
          "display_name": "Test templates",
          "templates": [
            {
              "family_id": "test.console",
              "variant_id": "windows",
              "template_id": "test.console.windows",
              "name": "Test Console",
              "description": "A test package.",
              "version": "1.0.0",
              "target_os": "windows",
              "build_system": "cmake",
              "language": "cpp",
              "package_path": "packages/test.console.windows-1.0.0.zip",
              "license_summary": "Generated source: MIT",
              "package_sha256": "{{checksum}}",
              "package_size_bytes": {{packageSize}}
            }
          ]
        }
        """;

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string path,
        string content)
    {
        var entry = archive.CreateEntry(path);
        await using var writer = new StreamWriter(
            entry.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(content);
    }

    private sealed class RemoteRegistryFixture : IDisposable
    {
        private readonly TestPackage package;
        private readonly TemporaryDirectory temporaryDirectory = new();

        public RemoteRegistryFixture(
            string? checksumOverride = null,
            bool signed = false)
        {
            package = new TestPackage(
                TestManifests.Valid,
                new Dictionary<string, byte[]>
                {
                    ["main.cpp.sbn"] = TestPackage.Text(
                        "int main() { return 0; }"),
                });
            var archivePath = Path.Combine(temporaryDirectory.Path, "package.zip");
            ZipFile.CreateFromDirectory(
                package.RootPath,
                archivePath,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);
            var archiveBytes = File.ReadAllBytes(archivePath);
            var checksum = checksumOverride ??
                Convert.ToHexStringLower(SHA256.HashData(archiveBytes));
            var indexJson = CreateIndexJson(checksum, archiveBytes.LongLength);

            RegistryTrustPolicy? trustPolicy = null;
            byte[]? signatureBytes = null;
            if (signed)
            {
                using var rsa = RSA.Create(2048);
                const string keyId = "tests-2026";
                trustPolicy = new RegistryTrustPolicy(
                    "tests.publisher",
                    ImmutableArray.Create(new RegistryTrustedKey(
                        keyId,
                        RegistrySignatureVerifier.RsaPkcs1Sha256,
                        Convert.ToBase64String(
                            rsa.ExportSubjectPublicKeyInfo()))));
                signatureBytes = Encoding.UTF8.GetBytes(CreateSignatureJson(
                    Encoding.UTF8.GetBytes(indexJson),
                    rsa,
                    trustPolicy.PublisherId,
                    keyId));
            }

            Source = new RegistrySource(
                "Remote tests",
                RegistrySourceKind.Remote,
                "https://registry.example/registry.json",
                TrustPolicy: trustPolicy);
            CacheRoot = Path.Combine(temporaryDirectory.Path, "cache");
            var responses = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [Source.Location] = Encoding.UTF8.GetBytes(indexJson),
                ["https://registry.example/packages/test.console.windows-1.0.0.zip"] =
                    archiveBytes,
            };
            if (signatureBytes is not null)
            {
                responses[$"{Source.Location}.sig.json"] = signatureBytes;
            }

            Responses = responses;
        }

        public RegistrySource Source { get; }

        public string CacheRoot { get; }

        public IReadOnlyDictionary<string, byte[]> Responses { get; }

        public void Dispose()
        {
            package.Dispose();
            temporaryDirectory.Dispose();
        }
    }

    private static string CreateSignatureJson(
        byte[] indexBytes,
        RSA rsa,
        string publisherId,
        string keyId)
    {
        var hash = SHA256.HashData(indexBytes);
        var signature = rsa.SignHash(
            hash,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return JsonSerializer.Serialize(new
        {
            schema_version = RegistrySignatureVerifier.SupportedSchemaVersion,
            publisher_id = publisherId,
            key_id = keyId,
            algorithm = RegistrySignatureVerifier.RsaPkcs1Sha256,
            index_sha256 = Convert.ToHexStringLower(hash),
            signature = Convert.ToBase64String(signature),
        });
    }

    private sealed class MappedHttpHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, byte[]> responses;

        public MappedHttpHandler(IReadOnlyDictionary<string, byte[]> responses)
        {
            this.responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = request.RequestUri!.AbsoluteUri;
            if (!responses.TryGetValue(location, out var bytes))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes),
            });
        }
    }

    private sealed class RejectingHttpHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            throw new HttpRequestException("Network access was not expected.");
        }
    }
}
