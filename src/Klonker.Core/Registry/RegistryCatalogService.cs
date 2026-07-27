using System.Collections.Immutable;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Klonker.Core.Diagnostics;
using Klonker.Core.Templates;

namespace Klonker.Core.Registry;

public sealed class RegistryCatalogService
{
    private const int MaximumIndexBytes = 5 * 1024 * 1024;
    private const long MaximumPackageBytes = 128L * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly HttpClient httpClient;

    public RegistryCatalogService(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<OperationResult<ResolvedRegistryCatalog>> LoadAsync(
        IEnumerable<RegistrySource> sources,
        RegistryCatalogOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CacheRoot);

        var enabledSources = sources
            .Where(source => source.Enabled)
            .ToArray();
        if (enabledSources.Length == 0)
        {
            return Failure<ResolvedRegistryCatalog>(
                "registry.sources_empty",
                "No template registry sources are enabled.");
        }

        var cacheRoot = Path.GetFullPath(options.CacheRoot);
        Directory.CreateDirectory(cacheRoot);

        var templates = ImmutableArray.CreateBuilder<RegistryTemplatePackage>();
        var issues = new List<ValidationIssue>();
        var failedSourceIssues = new List<(RegistrySource Source, ValidationIssue Issue)>();
        var registryIds = new HashSet<string>(StringComparer.Ordinal);
        var qualifiedTemplates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in enabledSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = source.Kind switch
            {
                RegistrySourceKind.Local => await LoadLocalSourceAsync(
                    source,
                    cacheRoot,
                    cancellationToken).ConfigureAwait(false),
                RegistrySourceKind.Remote => await LoadRemoteSourceAsync(
                    source,
                    cacheRoot,
                    options.Offline,
                    cancellationToken).ConfigureAwait(false),
                _ => Failure<ResolvedRegistryCatalog>(
                    "registry.source_kind_unsupported",
                    $"Registry source '{source.Name}' has an unsupported kind."),
            };

            if (!result.IsSuccess)
            {
                failedSourceIssues.AddRange(
                    result.Issues.Select(issue => (source, issue)));
                continue;
            }

            issues.AddRange(result.Issues);
            var sourceTemplates = result.Value!.Templates;
            var registryId = sourceTemplates.FirstOrDefault()?.RegistryId;
            if (registryId is null)
            {
                failedSourceIssues.Add((
                    source,
                    Error(
                        "registry.source_empty",
                        $"Registry source '{source.Name}' contains no usable templates.")));
                continue;
            }

            if (!registryIds.Add(registryId))
            {
                failedSourceIssues.Add((
                    source,
                    Error(
                        "registry.id_duplicate",
                        $"Registry ID '{registryId}' is configured more than once.")));
                continue;
            }

            foreach (var template in sourceTemplates)
            {
                var key = $"{template.RegistryId}\n{template.Entry.TemplateId}";
                if (!qualifiedTemplates.Add(key))
                {
                    issues.Add(Warning(
                        "registry.qualified_template_duplicate",
                        $"Duplicate qualified template '{template.RegistryId}:{template.Entry.TemplateId}' was ignored."));
                    continue;
                }

                templates.Add(template);
            }
        }

        if (templates.Count == 0)
        {
            issues.AddRange(failedSourceIssues.Select(item =>
                item.Issue with
                {
                    Message = $"Registry '{item.Source.Name}': {item.Issue.Message}",
                }));
            if (!issues.Any(issue => issue.Severity == ValidationSeverity.Error))
            {
                issues.Add(Error(
                    "registry.catalog_empty",
                    "No usable templates were loaded from the configured registries."));
            }

            return new OperationResult<ResolvedRegistryCatalog>(null, issues);
        }

        issues.AddRange(failedSourceIssues.Select(item =>
            new ValidationIssue(
                ValidationSeverity.Warning,
                item.Issue.Code,
                $"Registry '{item.Source.Name}' was skipped: {item.Issue.Message}")));

        return new OperationResult<ResolvedRegistryCatalog>(
            new ResolvedRegistryCatalog(
                templates
                    .OrderBy(template => template.RegistryId, StringComparer.Ordinal)
                    .ThenBy(template => template.Entry.TemplateId, StringComparer.Ordinal)
                    .ToImmutableArray()),
            issues);
    }

    private static async Task<OperationResult<ResolvedRegistryCatalog>> LoadLocalSourceAsync(
        RegistrySource source,
        string cacheRoot,
        CancellationToken cancellationToken)
    {
        var registry = LocalRegistryLoader.Load(source.Location);
        if (!registry.IsSuccess)
        {
            return new OperationResult<ResolvedRegistryCatalog>(null, registry.Issues);
        }

        var templates = ImmutableArray.CreateBuilder<RegistryTemplatePackage>();
        var issues = new List<ValidationIssue>(registry.Issues);
        foreach (var entry in registry.Value!.Templates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var packagePath = LocalRegistryLoader.ResolvePackagePath(registry.Value, entry);
            if (!packagePath.IsSuccess)
            {
                issues.AddRange(ToWarnings(packagePath.Issues, entry.TemplateId));
                continue;
            }

            var verified = await PackageIntegrity.VerifyAsync(
                packagePath.Value!,
                entry,
                cancellationToken).ConfigureAwait(false);
            if (!verified.IsSuccess)
            {
                issues.AddRange(ToWarnings(verified.Issues, entry.TemplateId));
                continue;
            }

            var packageRoot = packagePath.Value!;
            if (File.Exists(packageRoot))
            {
                var extracted = await EnsureExtractedAsync(
                    packageRoot,
                    cacheRoot,
                    registry.Value.RegistryId,
                    entry,
                    cancellationToken).ConfigureAwait(false);
                if (!extracted.IsSuccess)
                {
                    issues.AddRange(ToWarnings(extracted.Issues, entry.TemplateId));
                    continue;
                }

                packageRoot = extracted.Value!;
            }

            var resolved = ResolveTemplatePackage(
                registry.Value.RegistryId,
                registry.Value.DisplayName,
                entry,
                packageRoot);
            if (!resolved.IsSuccess)
            {
                issues.AddRange(ToWarnings(resolved.Issues, entry.TemplateId));
                continue;
            }

            templates.Add(resolved.Value!);
        }

        if (templates.Count == 0)
        {
            return new OperationResult<ResolvedRegistryCatalog>(
                null,
                issues.Append(Error(
                    "registry.source_empty",
                    $"Local registry '{source.Name}' contains no usable templates.")));
        }

        return new OperationResult<ResolvedRegistryCatalog>(
            new ResolvedRegistryCatalog(templates.ToImmutable()),
            issues);
    }

    private async Task<OperationResult<ResolvedRegistryCatalog>> LoadRemoteSourceAsync(
        RegistrySource source,
        string cacheRoot,
        bool offline,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(source.Location, UriKind.Absolute, out var indexUri) ||
            !indexUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return Failure<ResolvedRegistryCatalog>(
                "registry.remote_url_invalid",
                $"Remote registry '{source.Name}' must use an absolute HTTPS index URL.");
        }

        var indexResult = await LoadRemoteIndexAsync(
            indexUri,
            cacheRoot,
            offline,
            cancellationToken).ConfigureAwait(false);
        if (!indexResult.IsSuccess)
        {
            return new OperationResult<ResolvedRegistryCatalog>(null, indexResult.Issues);
        }

        var templates = ImmutableArray.CreateBuilder<RegistryTemplatePackage>();
        var issues = new List<ValidationIssue>(indexResult.Issues);
        foreach (var entry in indexResult.Value!.Index.Templates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var archive = await EnsureRemoteArchiveAsync(
                indexUri,
                indexResult.Value.Index.RegistryId,
                entry,
                cacheRoot,
                offline,
                cancellationToken).ConfigureAwait(false);
            if (!archive.IsSuccess)
            {
                issues.AddRange(ToWarnings(archive.Issues, entry.TemplateId));
                continue;
            }

            issues.AddRange(archive.Issues);
            var extracted = await EnsureExtractedAsync(
                archive.Value!,
                cacheRoot,
                indexResult.Value.Index.RegistryId,
                entry,
                cancellationToken).ConfigureAwait(false);
            if (!extracted.IsSuccess)
            {
                issues.AddRange(ToWarnings(extracted.Issues, entry.TemplateId));
                continue;
            }

            var resolved = ResolveTemplatePackage(
                indexResult.Value.Index.RegistryId,
                indexResult.Value.Index.DisplayName,
                entry,
                extracted.Value!);
            if (!resolved.IsSuccess)
            {
                issues.AddRange(ToWarnings(resolved.Issues, entry.TemplateId));
                continue;
            }

            templates.Add(resolved.Value!);
        }

        if (templates.Count == 0)
        {
            return new OperationResult<ResolvedRegistryCatalog>(
                null,
                issues.Append(Error(
                    "registry.source_empty",
                    $"Remote registry '{source.Name}' contains no usable cached templates.")));
        }

        return new OperationResult<ResolvedRegistryCatalog>(
            new ResolvedRegistryCatalog(templates.ToImmutable()),
            issues);
    }

    private async Task<OperationResult<RemoteIndex>> LoadRemoteIndexAsync(
        Uri indexUri,
        string cacheRoot,
        bool offline,
        CancellationToken cancellationToken)
    {
        var sourceKey = HashText(indexUri.AbsoluteUri);
        var indexDirectory = Path.Combine(cacheRoot, "v1", "indexes");
        var cachedPath = Path.Combine(indexDirectory, $"{sourceKey}.json");
        Directory.CreateDirectory(indexDirectory);

        if (!offline)
        {
            var downloaded = await DownloadIndexAsync(indexUri, cancellationToken)
                .ConfigureAwait(false);
            if (downloaded.IsSuccess)
            {
                var parsed = RegistryIndexLoader.Parse(
                    downloaded.Value!.Json,
                    indexUri.AbsoluteUri);
                if (parsed.IsSuccess)
                {
                    await WriteFileAtomicallyAsync(
                        cachedPath,
                        downloaded.Value.Bytes,
                        cancellationToken).ConfigureAwait(false);
                    return new OperationResult<RemoteIndex>(
                        new RemoteIndex(parsed.Value!, WasCached: false),
                        parsed.Issues);
                }

                downloaded = new OperationResult<DownloadedIndex>(
                    null,
                    parsed.Issues);
            }

            var cached = await TryLoadCachedIndexAsync(
                cachedPath,
                indexUri,
                cancellationToken).ConfigureAwait(false);
            if (cached.IsSuccess)
            {
                return new OperationResult<RemoteIndex>(
                    new RemoteIndex(cached.Value!, WasCached: true),
                    cached.Issues.Append(Warning(
                        "registry.index_cache_fallback",
                        $"The remote registry could not be refreshed; Klonker is using its cached index. {FirstMessage(downloaded.Issues)}")));
            }

            return new OperationResult<RemoteIndex>(
                null,
                downloaded.Issues.Concat(cached.Issues));
        }

        var offlineIndex = await TryLoadCachedIndexAsync(
            cachedPath,
            indexUri,
            cancellationToken).ConfigureAwait(false);
        if (!offlineIndex.IsSuccess)
        {
            return new OperationResult<RemoteIndex>(
                null,
                offlineIndex.Issues.Append(Error(
                    "registry.offline_index_missing",
                    "Offline mode is enabled and no valid cached registry index is available.")));
        }

        return new OperationResult<RemoteIndex>(
            new RemoteIndex(offlineIndex.Value!, WasCached: true),
            offlineIndex.Issues.Append(new ValidationIssue(
                ValidationSeverity.Information,
                "registry.offline_cache",
                "Offline mode is enabled; the cached registry index and packages are being used.")));
    }

    private async Task<OperationResult<DownloadedIndex>> DownloadIndexAsync(
        Uri indexUri,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, indexUri);
            request.Headers.UserAgent.ParseAdd("Klonker/0.1");
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
            {
                return Failure<DownloadedIndex>(
                    "registry.http_status",
                    $"The registry server returned HTTP {(int)response.StatusCode}.");
            }

            if (response.Content.Headers.ContentLength > MaximumIndexBytes)
            {
                return Failure<DownloadedIndex>(
                    "registry.index_too_large",
                    $"Registry indexes may not exceed {MaximumIndexBytes} bytes.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(
                cancellationToken).ConfigureAwait(false);
            var bytesResult = await ReadLimitedAsync(
                stream,
                MaximumIndexBytes,
                cancellationToken).ConfigureAwait(false);
            if (!bytesResult.IsSuccess)
            {
                return new OperationResult<DownloadedIndex>(null, bytesResult.Issues);
            }

            string json;
            try
            {
                json = StrictUtf8.GetString(bytesResult.Value!.Bytes);
            }
            catch (DecoderFallbackException)
            {
                return Failure<DownloadedIndex>(
                    "registry.index_utf8",
                    "The registry index is not valid UTF-8.");
            }

            return new OperationResult<DownloadedIndex>(
                new DownloadedIndex(json, bytesResult.Value.Bytes),
                []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException)
        {
            return Failure<DownloadedIndex>(
                "registry.download_failed",
                $"The registry index could not be downloaded: {exception.Message}");
        }
    }

    private static async Task<OperationResult<RegistryIndex>> TryLoadCachedIndexAsync(
        string cachedPath,
        Uri indexUri,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(cachedPath))
        {
            return Failure<RegistryIndex>(
                "registry.index_cache_missing",
                "No cached registry index is available.");
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(cachedPath, cancellationToken)
                .ConfigureAwait(false);
            if (bytes.Length > MaximumIndexBytes)
            {
                return Failure<RegistryIndex>(
                    "registry.index_cache_invalid",
                    "The cached registry index exceeds the allowed size.");
            }

            var json = StrictUtf8.GetString(bytes);
            return RegistryIndexLoader.Parse(json, indexUri.AbsoluteUri);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                DecoderFallbackException)
        {
            return Failure<RegistryIndex>(
                "registry.index_cache_invalid",
                $"The cached registry index could not be read: {exception.Message}");
        }
    }

    private async Task<OperationResult<string>> EnsureRemoteArchiveAsync(
        Uri indexUri,
        string registryId,
        RegistryTemplateEntry entry,
        string cacheRoot,
        bool offline,
        CancellationToken cancellationToken)
    {
        var cacheKey = PackageCacheKey(registryId, entry);
        var packageDirectory = Path.Combine(cacheRoot, "v1", "packages", cacheKey);
        var archivePath = Path.Combine(packageDirectory, "package.zip");
        Directory.CreateDirectory(packageDirectory);

        if (File.Exists(archivePath))
        {
            var cached = await PackageIntegrity.VerifyAsync(
                archivePath,
                entry,
                cancellationToken).ConfigureAwait(false);
            if (cached.IsSuccess)
            {
                return new OperationResult<string>(archivePath, cached.Issues);
            }

            File.Delete(archivePath);
        }

        if (offline)
        {
            return Failure<string>(
                "registry.offline_package_missing",
                $"Package '{entry.TemplateId}' is not available in the offline cache.");
        }

        var packageUri = new Uri(indexUri, entry.PackagePath);
        if (!packageUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return Failure<string>(
                "registry.package_url_invalid",
                $"Package '{entry.TemplateId}' must resolve to an HTTPS URL.");
        }

        return await DownloadPackageAsync(
            packageUri,
            archivePath,
            entry,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult<string>> DownloadPackageAsync(
        Uri packageUri,
        string destination,
        RegistryTemplateEntry entry,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{destination}.{Guid.NewGuid():N}.download";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, packageUri);
            request.Headers.UserAgent.ParseAdd("Klonker/0.1");
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
            {
                return Failure<string>(
                    "registry.package_http_status",
                    $"Package '{entry.TemplateId}' returned HTTP {(int)response.StatusCode}.");
            }

            if (entry.PackageSizeBytes > MaximumPackageBytes ||
                response.Content.Headers.ContentLength > MaximumPackageBytes)
            {
                return Failure<string>(
                    "registry.package_too_large",
                    $"Package '{entry.TemplateId}' exceeds the {MaximumPackageBytes} byte limit.");
            }

            await using var input = await response.Content.ReadAsStreamAsync(
                cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total = checked(total + read);
                if (total > MaximumPackageBytes ||
                    total > entry.PackageSizeBytes)
                {
                    return Failure<string>(
                        "registry.package_size_mismatch",
                        $"Package '{entry.TemplateId}' exceeded its declared size.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            await output.DisposeAsync().ConfigureAwait(false);
            var verified = await PackageIntegrity.VerifyAsync(
                temporaryPath,
                entry,
                cancellationToken).ConfigureAwait(false);
            if (!verified.IsSuccess)
            {
                return new OperationResult<string>(null, verified.Issues);
            }

            File.Move(temporaryPath, destination, overwrite: true);
            return new OperationResult<string>(
                destination,
                [
                    new ValidationIssue(
                        ValidationSeverity.Information,
                        "registry.package_cached",
                        $"Cached package '{entry.TemplateId}' for offline use."),
                ]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
                IOException or
                UnauthorizedAccessException)
        {
            return Failure<string>(
                "registry.package_download_failed",
                $"Package '{entry.TemplateId}' could not be downloaded: {exception.Message}");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<OperationResult<string>> EnsureExtractedAsync(
        string archivePath,
        string cacheRoot,
        string registryId,
        RegistryTemplateEntry entry,
        CancellationToken cancellationToken)
    {
        var cacheKey = PackageCacheKey(registryId, entry);
        var packageDirectory = Path.Combine(cacheRoot, "v1", "packages", cacheKey);
        var extractedPath = Path.Combine(packageDirectory, "package");
        var markerPath = Path.Combine(packageDirectory, "complete.sha256");
        string? cachedChecksum = null;
        try
        {
            if (File.Exists(markerPath))
            {
                cachedChecksum = (await File.ReadAllTextAsync(
                    markerPath,
                    cancellationToken).ConfigureAwait(false)).Trim();
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure<string>(
                "registry.cache_read_failed",
                $"The package cache marker could not be read: {exception.Message}");
        }

        if (Directory.Exists(extractedPath) &&
            string.Equals(
                cachedChecksum,
                entry.PackageSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return new OperationResult<string>(extractedPath, []);
        }

        var extraction = await PackageArchiveExtractor.ExtractAsync(
            archivePath,
            extractedPath,
            cancellationToken).ConfigureAwait(false);
        if (!extraction.IsSuccess)
        {
            return extraction;
        }

        await WriteFileAtomicallyAsync(
            markerPath,
            StrictUtf8.GetBytes(entry.PackageSha256),
            cancellationToken).ConfigureAwait(false);
        return extraction;
    }

    private static OperationResult<RegistryTemplatePackage> ResolveTemplatePackage(
        string registryId,
        string registryDisplayName,
        RegistryTemplateEntry entry,
        string packageRoot)
    {
        var package = TemplatePackageLoader.Load(packageRoot);
        if (!package.IsSuccess)
        {
            return new OperationResult<RegistryTemplatePackage>(null, package.Issues);
        }

        var manifest = package.Value!.Manifest;
        if (!string.Equals(manifest.Id, entry.TemplateId, StringComparison.Ordinal) ||
            !string.Equals(manifest.FamilyId, entry.FamilyId, StringComparison.Ordinal) ||
            !string.Equals(manifest.VariantId, entry.VariantId, StringComparison.Ordinal) ||
            !string.Equals(manifest.Version, entry.Version, StringComparison.Ordinal) ||
            (manifest.Language != "unknown" &&
             entry.Language != "unknown" &&
             !string.Equals(
                 manifest.Language,
                 entry.Language,
                 StringComparison.Ordinal)))
        {
            return Failure<RegistryTemplatePackage>(
                "registry.package_identity_mismatch",
                $"Registry entry '{entry.TemplateId}' does not match its package manifest.");
        }

        var qualifiedPackage = package.Value with { RegistryId = registryId };
        return new OperationResult<RegistryTemplatePackage>(
            new RegistryTemplatePackage(
                registryId,
                registryDisplayName,
                entry,
                qualifiedPackage),
            package.Issues);
    }

    private static async Task WriteFileAtomicallyAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(parent);
        var temporaryPath = Path.Combine(
            parent,
            $".{Path.GetFileName(path)}-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken)
                .ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<OperationResult<BytePayload>> ReadLimitedAsync(
        Stream stream,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return new OperationResult<BytePayload>(
                    new BytePayload(output.ToArray()),
                    []);
            }

            if (output.Length + read > maximumBytes)
            {
                return Failure<BytePayload>(
                    "registry.index_too_large",
                    $"Registry indexes may not exceed {maximumBytes} bytes.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static IEnumerable<ValidationIssue> ToWarnings(
        IEnumerable<ValidationIssue> issues,
        string templateId) =>
        issues.Select(issue => new ValidationIssue(
            issue.Severity == ValidationSeverity.Information
                ? ValidationSeverity.Information
                : ValidationSeverity.Warning,
            issue.Code,
            $"Template '{templateId}' was skipped: {issue.Message}",
            issue.ParameterId,
            issue.Path));

    private static string PackageCacheKey(
        string registryId,
        RegistryTemplateEntry entry) =>
        HashText(
            $"{registryId}\n{entry.TemplateId}\n{entry.Version}\n{entry.PackageSha256}");

    private static string HashText(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(StrictUtf8.GetBytes(value)));

    private static string FirstMessage(IEnumerable<ValidationIssue> issues) =>
        issues.Select(issue => issue.Message).FirstOrDefault() ??
        "The remote source was unavailable.";

    private static ValidationIssue Error(string code, string message) =>
        new(ValidationSeverity.Error, code, message);

    private static ValidationIssue Warning(string code, string message) =>
        new(ValidationSeverity.Warning, code, message);

    private static OperationResult<T> Failure<T>(string code, string message)
        where T : class =>
        new(null, [Error(code, message)]);

    private sealed record RemoteIndex(RegistryIndex Index, bool WasCached);

    private sealed record DownloadedIndex(string Json, byte[] Bytes);

    private sealed record BytePayload(byte[] Bytes);
}
