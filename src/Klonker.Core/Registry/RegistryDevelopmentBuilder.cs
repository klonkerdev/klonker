using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Klonker.Core.Authoring;
using Klonker.Core.Diagnostics;
using Klonker.Core.Modules;
using Klonker.Core.Templates;
using Tomlyn;
using Tomlyn.Model;

namespace Klonker.Core.Registry;

public static partial class RegistryDevelopmentBuilder
{
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<OperationResult<RegistryBuildOutput>> BuildAsync(
        RegistryBuildRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<ValidationIssue>();
        string sourceRoot;
        string outputRoot;
        try
        {
            sourceRoot = Path.GetFullPath(request.SourceRoot);
            outputRoot = Path.GetFullPath(request.OutputRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            return Failure(
                "registry.build_path_invalid",
                $"A registry build path is invalid: {exception.Message}",
                request.SourceRoot);
        }

        if (!Directory.Exists(sourceRoot))
        {
            return Failure(
                "registry.build_source_missing",
                "The registry source folder does not exist.",
                sourceRoot);
        }

        if (new DirectoryInfo(sourceRoot).Attributes.HasFlag(
                FileAttributes.ReparsePoint))
        {
            return Failure(
                "registry.build_source_reparse",
                "The registry source folder cannot be a symbolic link or reparse point.",
                sourceRoot);
        }

        if (PathsEqual(sourceRoot, outputRoot))
        {
            return Failure(
                "registry.build_output_is_source",
                "Build output must be a dedicated folder such as <workspace>/dist.",
                outputRoot);
        }

        var definitionPath = Path.Combine(sourceRoot, "registry.toml");
        var definition = ParseToml(definitionPath, issues);
        if (definition is null)
        {
            return new OperationResult<RegistryBuildOutput>(null, issues);
        }

        var registryId = RequiredString(
            definition,
            "registry_id",
            definitionPath,
            issues);
        var displayName = RequiredString(
            definition,
            "display_name",
            definitionPath,
            issues);
        var publisherId = OptionalString(definition, "publisher_id");
        var signingKeyId = OptionalString(definition, "signing_key_id");
        if (!definition.TryGetValue("schema_version", out var schema) ||
            schema is not long schemaVersion ||
            schemaVersion != 0)
        {
            issues.Add(Error(
                "registry.build_schema_invalid",
                "registry.toml schema_version must be the integer 0.",
                definitionPath));
        }

        var signing = !string.IsNullOrWhiteSpace(request.SigningKeyPath);
        if (signing &&
            (string.IsNullOrWhiteSpace(publisherId) ||
             string.IsNullOrWhiteSpace(signingKeyId)))
        {
            issues.Add(Error(
                "registry.build_signing_identity_required",
                "registry.toml must declare publisher_id and signing_key_id when signing.",
                definitionPath));
        }

        if (signing && !File.Exists(request.SigningKeyPath))
        {
            issues.Add(Error(
                "registry.build_signing_key_missing",
                "The selected private signing key does not exist.",
                request.SigningKeyPath));
        }

        var templatesRoot = Path.Combine(sourceRoot, "templates");
        var packageRoots = Directory.Exists(templatesRoot)
            ? DiscoverPackageRoots(templatesRoot, issues)
            : [];
        var modulesRoot = Path.Combine(sourceRoot, "modules");
        var moduleRoots = Directory.Exists(modulesRoot)
            ? DiscoverModuleRoots(modulesRoot, issues)
            : [];
        if (packageRoots.Length == 0 && moduleRoots.Length == 0)
        {
            issues.Add(Error(
                "registry.build_package_required",
                "No source packages or modules were found. Add templates/<namespace>/<package>/package.toml or modules/<namespace>/<module>/module.toml.",
                sourceRoot));
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<RegistryBuildOutput>(null, issues);
        }

        var outputParent = Directory.GetParent(outputRoot);
        if (outputParent is null)
        {
            return Failure(
                "registry.build_output_parent_invalid",
                "The output folder must have a parent directory.",
                outputRoot);
        }

        Directory.CreateDirectory(outputParent.FullName);
        var stagingRoot = Path.Combine(
            outputParent.FullName,
            $".klonker-{Path.GetFileName(outputRoot)}-{Guid.NewGuid():N}.staging");
        var backupRoot = Path.Combine(
            outputParent.FullName,
            $".klonker-{Path.GetFileName(outputRoot)}-{Guid.NewGuid():N}.backup");
        var movedExistingOutput = false;

        try
        {
            Directory.CreateDirectory(stagingRoot);
            var packagesOutput = Path.Combine(stagingRoot, "packages");
            Directory.CreateDirectory(packagesOutput);
            var entries = new List<RegistryEntryDto>();
            var moduleEntries = new List<RegistryModuleEntryDto>();
            var templateIds = new HashSet<string>(StringComparer.Ordinal);
            var moduleIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var packageRoot in packageRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var built = await BuildPackageAsync(
                        packageRoot,
                        templatesRoot,
                        packagesOutput,
                        templateIds,
                        cancellationToken)
                    .ConfigureAwait(false);
                issues.AddRange(built.Issues);
                if (built.IsSuccess)
                {
                    entries.AddRange(built.Value!.Entries);
                }
            }

            foreach (var moduleRoot in moduleRoots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var built = await BuildModuleAsync(
                        moduleRoot,
                        modulesRoot,
                        packagesOutput,
                        moduleIds,
                        cancellationToken)
                    .ConfigureAwait(false);
                issues.AddRange(built.Issues);
                if (built.IsSuccess)
                {
                    moduleEntries.Add(built.Value!);
                }
            }

            if (issues.Any(issue =>
                    issue.Severity == ValidationSeverity.Error))
            {
                return new OperationResult<RegistryBuildOutput>(null, issues);
            }

            var index = new RegistryIndexDto(
                1,
                registryId!,
                displayName!,
                entries
                    .OrderBy(entry => entry.TemplateId, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Version, StringComparer.Ordinal)
                    .ToImmutableArray(),
                moduleEntries
                    .OrderBy(entry => entry.ModuleId, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Version, StringComparer.Ordinal)
                    .ToImmutableArray());
            var indexJson = JsonSerializer.Serialize(index, JsonOptions)
                .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
            var indexBytes = Utf8.GetBytes(indexJson);
            var indexPath = Path.Combine(stagingRoot, "registry.json");
            await File.WriteAllBytesAsync(
                    indexPath,
                    indexBytes,
                    cancellationToken)
                .ConfigureAwait(false);

            if (signing)
            {
                var signed = await CreateSignatureAsync(
                        indexBytes,
                        publisherId!,
                        signingKeyId!,
                        request.SigningKeyPath!,
                        cancellationToken)
                    .ConfigureAwait(false);
                issues.AddRange(signed.Issues);
                if (!signed.IsSuccess)
                {
                    return new OperationResult<RegistryBuildOutput>(
                        null,
                        issues);
                }

                await File.WriteAllTextAsync(
                        Path.Combine(
                            stagingRoot,
                            "registry.json.sig.json"),
                        signed.Value!,
                        Utf8,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            var loaded = LocalRegistryLoader.Load(indexPath);
            issues.AddRange(loaded.Issues);
            if (!loaded.IsSuccess)
            {
                return new OperationResult<RegistryBuildOutput>(null, issues);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(outputRoot))
            {
                Directory.Move(outputRoot, backupRoot);
                movedExistingOutput = true;
            }

            Directory.Move(stagingRoot, outputRoot);
            if (movedExistingOutput && Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot, recursive: true);
                movedExistingOutput = false;
            }

            return new OperationResult<RegistryBuildOutput>(
                new RegistryBuildOutput(
                    Path.Combine(outputRoot, "registry.json"),
                    entries.Count,
                    signing,
                    moduleEntries.Count),
                issues);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                CryptographicException)
        {
            issues.Add(Error(
                "registry.build_failed",
                $"The registry build failed before installation: {exception.Message}",
                sourceRoot));
            return new OperationResult<RegistryBuildOutput>(null, issues);
        }
        finally
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }

            if (movedExistingOutput &&
                Directory.Exists(backupRoot) &&
                !Directory.Exists(outputRoot))
            {
                Directory.Move(backupRoot, outputRoot);
            }
        }
    }

    private static async Task<OperationResult<RegistryModuleEntryDto>>
        BuildModuleAsync(
        string moduleRoot,
        string modulesRoot,
        string packagesOutput,
        HashSet<string> moduleIds,
        CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        var loaded = ModulePackageLoader.Load(moduleRoot);
        issues.AddRange(loaded.Issues);
        if (!loaded.IsSuccess)
        {
            return new OperationResult<RegistryModuleEntryDto>(null, issues);
        }

        var relative = Path.GetRelativePath(modulesRoot, moduleRoot)
            .Replace('\\', '/');
        var pathParts = relative.Split('/');
        var expectedId = pathParts.Length == 2
            ? $"{pathParts[0]}.{pathParts[1]}"
            : string.Empty;
        var manifest = loaded.Value!.Manifest;
        if (pathParts.Length != 2 ||
            !string.Equals(manifest.Id, expectedId, StringComparison.Ordinal))
        {
            issues.Add(Error(
                "registry.build_module_identity_mismatch",
                "Module ID must match its modules/<namespace>/<module> folders.",
                Path.Combine(moduleRoot, "module.toml")));
        }

        var versionedId = $"{manifest.Id}\n{manifest.Version}";
        if (!moduleIds.Add(versionedId))
        {
            issues.Add(Error(
                "registry.build_module_duplicate",
                $"Module ID '{manifest.Id}' version '{manifest.Version}' is duplicated.",
                moduleRoot));
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<RegistryModuleEntryDto>(null, issues);
        }

        var runtimeRoot = Path.Combine(
            packagesOutput,
            $"{manifest.Id}-{manifest.Version}");
        Directory.CreateDirectory(runtimeRoot);
        CopyTree(moduleRoot, runtimeRoot, _ => true);

        var runtimePackage = ModulePackageLoader.Load(runtimeRoot);
        issues.AddRange(runtimePackage.Issues);
        if (!runtimePackage.IsSuccess)
        {
            return new OperationResult<RegistryModuleEntryDto>(null, issues);
        }

        var artifact = await PackageIntegrity.InspectAsync(
                runtimeRoot,
                cancellationToken)
            .ConfigureAwait(false);
        issues.AddRange(artifact.Issues);
        if (!artifact.IsSuccess)
        {
            return new OperationResult<RegistryModuleEntryDto>(null, issues);
        }

        return new OperationResult<RegistryModuleEntryDto>(
            new RegistryModuleEntryDto(
                manifest.Id,
                manifest.Name,
                manifest.Description,
                manifest.Version,
                manifest.Language,
                $"packages/{manifest.Id}-{manifest.Version}",
                manifest.SourceLicense,
                artifact.Value!.Sha256,
                artifact.Value.SizeBytes,
                manifest.Tags),
            issues);
    }

    private static async Task<OperationResult<PackageBuildOutput>>
        BuildPackageAsync(
        string packageRoot,
        string templatesRoot,
        string packagesOutput,
        HashSet<string> templateIds,
        CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        var inspected = ExistingTemplateInspector.Inspect(packageRoot);
        issues.AddRange(inspected.Issues);
        if (inspected.Kind != ExistingTemplateKind.RegistrySourcePackage ||
            inspected.HasErrors)
        {
            return new OperationResult<PackageBuildOutput>(
                null,
                issues);
        }

        var packagePath = Path.Combine(packageRoot, "package.toml");
        var packageText = await File.ReadAllTextAsync(
                packagePath,
                cancellationToken)
            .ConfigureAwait(false);
        var package = ParseToml(packagePath, issues);
        if (package is null)
        {
            return new OperationResult<PackageBuildOutput>(
                null,
                issues);
        }

        var relativeManifest = Path.GetRelativePath(
                templatesRoot,
                packagePath)
            .Replace('\\', '/');
        var pathParts = relativeManifest.Split('/');
        var namespaceId = RequiredString(
            package,
            "namespace",
            packagePath,
            issues);
        var packageId = RequiredString(
            package,
            "id",
            packagePath,
            issues);
        var name = RequiredString(
            package,
            "name",
            packagePath,
            issues);
        _ = RequiredString(
            package,
            "description",
            packagePath,
            issues);
        var language = RequiredString(
            package,
            "language",
            packagePath,
            issues);
        var sourceLicense = RequiredString(
            package,
            "source_license",
            packagePath,
            issues);
        var licenseSummary = RequiredString(
            package,
            "license_summary",
            packagePath,
            issues);
        var logo = OptionalString(package, "logo");
        var packageTags = StringArray(package, "tags");
        if (pathParts.Length != 3 ||
            !string.Equals(
                namespaceId,
                pathParts[0],
                StringComparison.Ordinal) ||
            !string.Equals(
                packageId,
                pathParts[1],
                StringComparison.Ordinal))
        {
            issues.Add(Error(
                "registry.build_package_identity_mismatch",
                "Package namespace and ID must match its templates/<namespace>/<package> folders.",
                packagePath));
        }

        var variantsRoot = Path.Combine(packageRoot, "variants");
        var variantRoots = Directory.Exists(variantsRoot)
            ? Directory.EnumerateDirectories(variantsRoot)
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
        var result = ImmutableArray.CreateBuilder<RegistryEntryDto>();
        foreach (var variantRoot in variantRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var variantPath = Path.Combine(variantRoot, "variant.toml");
            var variantText = await File.ReadAllTextAsync(
                    variantPath,
                    cancellationToken)
                .ConfigureAwait(false);
            var variant = ParseToml(variantPath, issues);
            if (variant is null)
            {
                continue;
            }

            var variantId = RequiredString(
                variant,
                "id",
                variantPath,
                issues);
            var description = RequiredString(
                variant,
                "description",
                variantPath,
                issues);
            var version = RequiredString(
                variant,
                "version",
                variantPath,
                issues);
            var targetOs = RequiredString(
                variant,
                "target_os",
                variantPath,
                issues);
            var buildSystem = RequiredString(
                variant,
                "build_system",
                variantPath,
                issues);
            if (issues.Any(issue =>
                    issue.Severity == ValidationSeverity.Error))
            {
                continue;
            }

            var familyId = $"{namespaceId}.{packageId}";
            var templateId = $"{familyId}.{variantId}";
            if (!templateIds.Add(templateId))
            {
                issues.Add(Error(
                    "registry.build_template_duplicate",
                    $"Template ID '{templateId}' is duplicated.",
                    variantPath));
                continue;
            }

            var runtimeRoot = Path.Combine(
                packagesOutput,
                $"{templateId}-{version}");
            Directory.CreateDirectory(runtimeRoot);
            var combinedTags = packageTags
                .Concat(StringArray(variant, "tags"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal)
                .ToImmutableArray();
            var manifest = BuildRuntimeManifest(
                templateId,
                familyId,
                variantId!,
                name!,
                description!,
                version!,
                targetOs!,
                buildSystem!,
                language!,
                sourceLicense!,
                logo,
                combinedTags,
                ExtractTables(packageText),
                ExtractTables(variantText));
            await File.WriteAllTextAsync(
                    Path.Combine(runtimeRoot, "template.toml"),
                    manifest,
                    Utf8,
                    cancellationToken)
                .ConfigureAwait(false);

            CopyTree(
                packageRoot,
                runtimeRoot,
                relativePath =>
                    !relativePath.Equals(
                        "package.toml",
                        StringComparison.OrdinalIgnoreCase) &&
                    !relativePath.StartsWith(
                        "variants/",
                        StringComparison.OrdinalIgnoreCase) &&
                    !relativePath.StartsWith(
                        ".git/",
                        StringComparison.OrdinalIgnoreCase));
            CopyTree(
                variantRoot,
                runtimeRoot,
                relativePath => !relativePath.Equals(
                    "variant.toml",
                    StringComparison.OrdinalIgnoreCase));

            var loaded = TemplatePackageLoader.Load(runtimeRoot);
            issues.AddRange(loaded.Issues);
            if (!loaded.IsSuccess)
            {
                continue;
            }

            var artifact = await PackageIntegrity.InspectAsync(
                    runtimeRoot,
                    cancellationToken)
                .ConfigureAwait(false);
            issues.AddRange(artifact.Issues);
            if (!artifact.IsSuccess)
            {
                continue;
            }

            result.Add(new RegistryEntryDto(
                familyId,
                variantId!,
                templateId,
                name!,
                description!,
                version!,
                targetOs!,
                buildSystem!,
                language!,
                $"packages/{templateId}-{version}",
                licenseSummary!,
                artifact.Value!.Sha256,
                artifact.Value.SizeBytes));
        }

        return issues.Any(issue =>
                issue.Severity == ValidationSeverity.Error)
            ? new OperationResult<PackageBuildOutput>(
                null,
                issues)
            : new OperationResult<PackageBuildOutput>(
                new PackageBuildOutput(result.ToImmutable()),
                issues);
    }

    private static async Task<OperationResult<string>> CreateSignatureAsync(
        byte[] indexBytes,
        string publisherId,
        string keyId,
        string signingKeyPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var pem = await File.ReadAllTextAsync(
                    signingKeyPath,
                    cancellationToken)
                .ConfigureAwait(false);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            if (rsa.KeySize < 2048)
            {
                return SigningFailure(
                    "registry.build_signing_key_too_small",
                    "Registry signing keys must be at least 2048 bits.",
                    signingKeyPath);
            }

            var hash = SHA256.HashData(indexBytes);
            var signature = rsa.SignHash(
                hash,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            var json = JsonSerializer.Serialize(
                    new SignatureDto(
                        0,
                        publisherId,
                        keyId,
                        RegistrySignatureVerifier.RsaPkcs1Sha256,
                        Convert.ToHexStringLower(hash),
                        Convert.ToBase64String(signature)),
                    JsonOptions)
                .Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
            return new OperationResult<string>(json, []);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                CryptographicException)
        {
            return SigningFailure(
                "registry.build_signing_failed",
                $"The registry index could not be signed: {exception.Message}",
                signingKeyPath);
        }
    }

    private static string BuildRuntimeManifest(
        string templateId,
        string familyId,
        string variantId,
        string name,
        string description,
        string version,
        string targetOs,
        string buildSystem,
        string language,
        string sourceLicense,
        string? logo,
        ImmutableArray<string> tags,
        string packageTables,
        string variantTables)
    {
        var builder = new StringBuilder();
        builder.AppendLine("schema_version = 0");
        builder.AppendLine();
        AppendToml(builder, "id", templateId);
        AppendToml(builder, "family_id", familyId);
        AppendToml(builder, "variant_id", variantId);
        builder.AppendLine();
        AppendToml(builder, "name", name);
        AppendToml(builder, "description", description);
        AppendToml(builder, "version", version);
        builder.AppendLine();
        AppendToml(builder, "target_os", targetOs);
        AppendToml(builder, "build_system", buildSystem);
        AppendToml(builder, "language", language);
        builder.AppendLine();
        AppendToml(builder, "source_license", sourceLicense);
        if (!string.IsNullOrWhiteSpace(logo))
        {
            AppendToml(builder, "logo", logo);
        }

        builder.Append("tags = [")
            .Append(string.Join(", ", tags.Select(QuoteToml)))
            .AppendLine("]");
        if (!string.IsNullOrWhiteSpace(packageTables))
        {
            builder.AppendLine();
            builder.Append(packageTables.Trim()).AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(variantTables))
        {
            builder.AppendLine();
            builder.Append(variantTables.Trim()).AppendLine();
        }

        return builder.ToString().Replace(
            "\r\n",
            "\n",
            StringComparison.Ordinal);
    }

    private static string ExtractTables(string toml)
    {
        var match = TableHeaderRegex().Match(toml);
        return match.Success ? toml[match.Index..] : string.Empty;
    }

    private static void CopyTree(
        string sourceRoot,
        string destinationRoot,
        Func<string, bool> include)
    {
        var pending = new Stack<string>();
        pending.Push(sourceRoot);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var child in Directory
                         .EnumerateDirectories(directory)
                         .OrderDescending(StringComparer.Ordinal))
            {
                var relativeDirectory = Path.GetRelativePath(
                        sourceRoot,
                        child)
                    .Replace('\\', '/');
                if (relativeDirectory.Equals(
                        ".git",
                        StringComparison.OrdinalIgnoreCase) ||
                    relativeDirectory.StartsWith(
                        ".git/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (new DirectoryInfo(child).Attributes.HasFlag(
                        FileAttributes.ReparsePoint))
                {
                    throw new IOException(
                        "Registry packages cannot contain symbolic links or reparse points.");
                }

                pending.Push(child);
            }

            foreach (var sourcePath in Directory
                         .EnumerateFiles(directory)
                         .Order(StringComparer.Ordinal))
            {
                var sourceInfo = new FileInfo(sourcePath);
                if (sourceInfo.Attributes.HasFlag(
                        FileAttributes.ReparsePoint))
                {
                    throw new IOException(
                        "Registry packages cannot contain symbolic links or reparse points.");
                }

                var relative = Path.GetRelativePath(sourceRoot, sourcePath)
                    .Replace('\\', '/');
                if (!include(relative))
                {
                    continue;
                }

                var destinationPath = Path.Combine(
                    destinationRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(destinationPath))
                {
                    throw new IOException(
                        $"Shared and variant content both write '{relative}'.");
                }

                Directory.CreateDirectory(
                    Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath);
            }
        }
    }

    private static string[] DiscoverPackageRoots(
        string templatesRoot,
        List<ValidationIssue> issues)
    {
        if (Directory.EnumerateFiles(templatesRoot).Any())
        {
            issues.Add(Error(
                "registry.build_templates_root_files",
                "The templates folder may contain namespace directories only.",
                templatesRoot));
        }

        var packages = new List<string>();
        foreach (var namespaceRoot in Directory
                     .EnumerateDirectories(templatesRoot)
                     .Order(StringComparer.Ordinal))
        {
            if (new DirectoryInfo(namespaceRoot).Attributes.HasFlag(
                    FileAttributes.ReparsePoint))
            {
                issues.Add(Error(
                    "registry.build_reparse",
                    "Registry source folders cannot be symbolic links or reparse points.",
                    namespaceRoot));
                continue;
            }

            if (Directory.EnumerateFiles(namespaceRoot).Any())
            {
                issues.Add(Error(
                    "registry.build_namespace_files",
                    "Namespace folders may contain package directories only.",
                    namespaceRoot));
            }

            foreach (var packageRoot in Directory
                         .EnumerateDirectories(namespaceRoot)
                         .Order(StringComparer.Ordinal))
            {
                if (new DirectoryInfo(packageRoot).Attributes.HasFlag(
                        FileAttributes.ReparsePoint))
                {
                    issues.Add(Error(
                        "registry.build_reparse",
                        "Registry source folders cannot be symbolic links or reparse points.",
                        packageRoot));
                    continue;
                }

                var manifest = Path.Combine(packageRoot, "package.toml");
                if (!File.Exists(manifest))
                {
                    issues.Add(Error(
                        "registry.build_package_manifest_missing",
                        "Each package folder requires package.toml.",
                        packageRoot));
                    continue;
                }

                packages.Add(packageRoot);
            }
        }

        return packages.ToArray();
    }

    private static string[] DiscoverModuleRoots(
        string modulesRoot,
        List<ValidationIssue> issues)
    {
        if (Directory.EnumerateFiles(modulesRoot).Any())
        {
            issues.Add(Error(
                "registry.build_modules_root_files",
                "The modules folder may contain namespace directories only.",
                modulesRoot));
        }

        var modules = new List<string>();
        foreach (var namespaceRoot in Directory
                     .EnumerateDirectories(modulesRoot)
                     .Order(StringComparer.Ordinal))
        {
            if (new DirectoryInfo(namespaceRoot).Attributes.HasFlag(
                    FileAttributes.ReparsePoint))
            {
                issues.Add(Error(
                    "registry.build_reparse",
                    "Registry source folders cannot be symbolic links or reparse points.",
                    namespaceRoot));
                continue;
            }

            if (Directory.EnumerateFiles(namespaceRoot).Any())
            {
                issues.Add(Error(
                    "registry.build_module_namespace_files",
                    "Module namespace folders may contain module directories only.",
                    namespaceRoot));
            }

            foreach (var moduleRoot in Directory
                         .EnumerateDirectories(namespaceRoot)
                         .Order(StringComparer.Ordinal))
            {
                if (new DirectoryInfo(moduleRoot).Attributes.HasFlag(
                        FileAttributes.ReparsePoint))
                {
                    issues.Add(Error(
                        "registry.build_reparse",
                        "Registry source folders cannot be symbolic links or reparse points.",
                        moduleRoot));
                    continue;
                }

                if (!File.Exists(Path.Combine(moduleRoot, "module.toml")))
                {
                    issues.Add(Error(
                        "registry.build_module_manifest_missing",
                        "Each module folder requires module.toml.",
                        moduleRoot));
                    continue;
                }

                modules.Add(moduleRoot);
            }
        }

        return modules.ToArray();
    }

    private static TomlTable? ParseToml(
        string path,
        List<ValidationIssue> issues)
    {
        if (!File.Exists(path))
        {
            issues.Add(Error(
                "registry.build_manifest_missing",
                "A required registry manifest is missing.",
                path));
            return null;
        }

        try
        {
            return TomlSerializer.Deserialize<TomlTable>(
                File.ReadAllText(path));
        }
        catch (Exception exception) when (
            exception is Tomlyn.TomlException or
                IOException or
                UnauthorizedAccessException)
        {
            issues.Add(Error(
                "registry.build_manifest_invalid",
                $"The registry manifest could not be read: {exception.Message}",
                path));
            return null;
        }
    }

    private static string? RequiredString(
        TomlTable table,
        string property,
        string path,
        List<ValidationIssue> issues)
    {
        var value = OptionalString(table, property);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        issues.Add(Error(
            "registry.build_property_required",
            $"Property '{property}' must be a non-empty string.",
            path));
        return null;
    }

    private static string? OptionalString(
        TomlTable table,
        string property) =>
        table.TryGetValue(property, out var value) &&
        value is string text &&
        !string.IsNullOrWhiteSpace(text)
            ? text
            : null;

    private static ImmutableArray<string> StringArray(
        TomlTable table,
        string property)
    {
        if (!table.TryGetValue(property, out var value) ||
            value is not IEnumerable<object> items)
        {
            return [];
        }

        return items.OfType<string>().ToImmutableArray();
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            StringComparison.OrdinalIgnoreCase);

    private static void AppendToml(
        StringBuilder builder,
        string key,
        string value) =>
        builder.Append(key)
            .Append(" = ")
            .AppendLine(QuoteToml(value));

    private static string QuoteToml(string value) =>
        $"\"{value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static OperationResult<T> Failure<T>(
        string code,
        string message,
        string path)
        where T : class =>
        new(null, [Error(code, message, path)]);

    private static OperationResult<RegistryBuildOutput> Failure(
        string code,
        string message,
        string path) =>
        Failure<RegistryBuildOutput>(code, message, path);

    private static OperationResult<string> SigningFailure(
        string code,
        string message,
        string path) =>
        Failure<string>(code, message, path);

    private static ValidationIssue Error(
        string code,
        string message,
        string? path = null) =>
        new(ValidationSeverity.Error, code, message, Path: path);

    [GeneratedRegex(
        @"(?m)^[ \t]*\[\[(?:parameters|prerequisites)\]\][ \t]*\r?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TableHeaderRegex();

    private sealed record RegistryIndexDto(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("registry_id")] string RegistryId,
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("templates")]
        ImmutableArray<RegistryEntryDto> Templates,
        [property: JsonPropertyName("modules")]
        ImmutableArray<RegistryModuleEntryDto> Modules);

    private sealed record RegistryEntryDto(
        [property: JsonPropertyName("family_id")] string FamilyId,
        [property: JsonPropertyName("variant_id")] string VariantId,
        [property: JsonPropertyName("template_id")] string TemplateId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("target_os")] string TargetOs,
        [property: JsonPropertyName("build_system")] string BuildSystem,
        [property: JsonPropertyName("language")] string Language,
        [property: JsonPropertyName("package_path")] string PackagePath,
        [property: JsonPropertyName("license_summary")] string LicenseSummary,
        [property: JsonPropertyName("package_sha256")] string PackageSha256,
        [property: JsonPropertyName("package_size_bytes")] long PackageSizeBytes);

    private sealed record RegistryModuleEntryDto(
        [property: JsonPropertyName("module_id")] string ModuleId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("language")] string Language,
        [property: JsonPropertyName("package_path")] string PackagePath,
        [property: JsonPropertyName("license_summary")] string LicenseSummary,
        [property: JsonPropertyName("package_sha256")] string PackageSha256,
        [property: JsonPropertyName("package_size_bytes")] long PackageSizeBytes,
        [property: JsonPropertyName("tags")] ImmutableArray<string> Tags);

    private sealed record SignatureDto(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("publisher_id")] string PublisherId,
        [property: JsonPropertyName("key_id")] string KeyId,
        [property: JsonPropertyName("algorithm")] string Algorithm,
        [property: JsonPropertyName("index_sha256")] string IndexSha256,
        [property: JsonPropertyName("signature")] string Signature);

    private sealed record PackageBuildOutput(
        ImmutableArray<RegistryEntryDto> Entries);
}
