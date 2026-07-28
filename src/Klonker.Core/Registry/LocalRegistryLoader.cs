using Klonker.Core.Diagnostics;
using Klonker.Core.Paths;

namespace Klonker.Core.Registry;

public static class LocalRegistryLoader
{
    public const int SupportedSchemaVersion = RegistryIndexLoader.SupportedSchemaVersion;

    public static OperationResult<LocalRegistryCatalog> Load(string registryJsonPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryJsonPath);

        var fullPath = Path.GetFullPath(registryJsonPath);
        if (!File.Exists(fullPath))
        {
            return Failure(
                "registry.not_found",
                "The local registry index could not be found.",
                fullPath);
        }

        string json;
        try
        {
            json = File.ReadAllText(fullPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "registry.read_failed",
                $"The local registry index could not be read: {exception.Message}",
                fullPath);
        }

        var indexResult = RegistryIndexLoader.Parse(json, fullPath);
        if (!indexResult.IsSuccess)
        {
            return new OperationResult<LocalRegistryCatalog>(null, indexResult.Issues);
        }

        var issues = new List<ValidationIssue>(indexResult.Issues);
        var registryRoot = Path.GetDirectoryName(fullPath)!;
        foreach (var entry in indexResult.Value!.Templates)
        {
            var packageResolution = SafePath.ResolveUnderRoot(
                registryRoot,
                entry.PackagePath);
            issues.AddRange(packageResolution.Issues);
            if (packageResolution.IsSuccess &&
                !Directory.Exists(packageResolution.Value) &&
                !File.Exists(packageResolution.Value))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "registry.package_not_found",
                    $"Package '{entry.PackagePath}' does not exist.",
                    Path: entry.PackagePath));
            }
        }

        foreach (var entry in indexResult.Value.Modules)
        {
            var packageResolution = SafePath.ResolveUnderRoot(
                registryRoot,
                entry.PackagePath);
            issues.AddRange(packageResolution.Issues);
            if (packageResolution.IsSuccess &&
                !Directory.Exists(packageResolution.Value) &&
                !File.Exists(packageResolution.Value))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Error,
                    "registry.package_not_found",
                    $"Module package '{entry.PackagePath}' does not exist.",
                    Path: entry.PackagePath));
            }
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<LocalRegistryCatalog>(null, issues);
        }

        return new OperationResult<LocalRegistryCatalog>(
            new LocalRegistryCatalog(
                indexResult.Value.SchemaVersion,
                indexResult.Value.RegistryId,
                indexResult.Value.DisplayName,
                registryRoot,
                indexResult.Value.Templates,
                indexResult.Value.Modules),
            issues);
    }

    public static OperationResult<string> ResolvePackagePath(
        LocalRegistryCatalog registry,
        RegistryTemplateEntry entry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(entry);
        return SafePath.ResolveUnderRoot(registry.RootPath, entry.PackagePath);
    }

    public static OperationResult<string> ResolvePackagePath(
        LocalRegistryCatalog registry,
        RegistryModuleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(entry);
        return SafePath.ResolveUnderRoot(registry.RootPath, entry.PackagePath);
    }

    private static OperationResult<LocalRegistryCatalog> Failure(
        string code,
        string message,
        string path) =>
        new(
            null,
            [
                new ValidationIssue(
                    ValidationSeverity.Error,
                    code,
                    message,
                    Path: path),
            ]);
}
