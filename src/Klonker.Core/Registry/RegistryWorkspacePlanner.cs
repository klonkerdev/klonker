using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using Klonker.Core.Authoring;
using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;
using Klonker.Core.Paths;

namespace Klonker.Core.Registry;

public static partial class RegistryWorkspacePlanner
{
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false);

    public static OperationResult<GenerationPlan> CreatePlan(
        RegistryWorkspaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<ValidationIssue>();
        ValidateId(request.RegistryId, "registry", issues);
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            issues.Add(Error(
                "registry.workspace_name_required",
                "Enter a registry display name."));
        }

        if (request.IsProduction)
        {
            ValidateId(request.PublisherId ?? string.Empty, "publisher", issues);
            ValidateId(request.SigningKeyId ?? string.Empty, "signing key", issues);
            if (string.IsNullOrWhiteSpace(request.PublicKeySpki))
            {
                issues.Add(Error(
                    "registry.workspace_public_key_required",
                    "Production signing setup requires a public publisher key."));
            }
        }

        var destination = TemplateAuthoringDestinationValidator.Validate(
            request.DestinationPath,
            request.InitialTemplatePath);
        issues.AddRange(destination.Issues);

        ExistingTemplateInspection? template = null;
        if (!string.IsNullOrWhiteSpace(request.InitialTemplatePath))
        {
            template = ExistingTemplateInspector.Inspect(
                request.InitialTemplatePath);
            issues.AddRange(template.Issues);
            if (template.Kind != ExistingTemplateKind.RegistrySourcePackage)
            {
                issues.Add(Error(
                    "registry.workspace_template_source_required",
                    "The initial template must use package.toml plus variants/<variant>/variant.toml source layout.",
                    request.InitialTemplatePath));
            }
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<GenerationPlan>(null, issues);
        }

        var files = new List<PlannedFile>
        {
            TextFile("registry.toml", BuildRegistryToml(request)),
            TextFile(".gitignore", BuildGitIgnore(request)),
            TextFile("README.md", BuildReadme(request)),
            TextFile("DEVELOPING.md", BuildDevelopmentGuide(request)),
        };
        var directories = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "templates",
            "modules",
        };

        if (request.IsProduction)
        {
            var keyPath = $"keys/{request.SigningKeyId}.spki";
            files.Add(TextFile(
                keyPath,
                request.PublicKeySpki!.Trim() + "\n"));
            directories.Add("keys");
            files.Add(TextFile(
                "PUBLISHING.md",
                BuildPublishingGuide(request)));
        }

        if (template is not null)
        {
            var metadata = template.Metadata!;
            var destinationRoot =
                $"templates/{metadata.NamespaceId}/{metadata.PackageId}";
            directories.Add($"templates/{metadata.NamespaceId}");
            directories.Add(destinationRoot);
            foreach (var sourceFile in template.Files.Order(StringComparer.Ordinal))
            {
                var normalized = SafePath.NormalizeRelative(sourceFile);
                issues.AddRange(normalized.Issues);
                if (!normalized.IsSuccess)
                {
                    continue;
                }

                var sourcePath = Path.Combine(
                    template.RootPath,
                    sourceFile.Replace(
                        '/',
                        Path.DirectorySeparatorChar));
                var destinationPath = $"{destinationRoot}/{sourceFile}";
                files.Add(new PlannedFile(
                    destinationPath,
                    File.ReadAllBytes(sourcePath).ToImmutableArray(),
                    IsText: false,
                    TextContent: null,
                    SourceTemplatePath: sourcePath));
                AddParents(destinationPath, directories);
            }
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<GenerationPlan>(null, issues);
        }

        var plan = new GenerationPlan(
            new TemplateIdentity(
                "registry-workspace",
                request.RegistryId,
                request.RegistryId,
                request.IsProduction ? "production" : "development",
                "1.0.0"),
            directories.Order(StringComparer.Ordinal).ToImmutableArray(),
            files.OrderBy(
                    file => file.RelativePath,
                    StringComparer.Ordinal)
                .ToImmutableArray(),
            issues.ToImmutableArray());
        return new OperationResult<GenerationPlan>(plan, issues);
    }

    private static string BuildRegistryToml(RegistryWorkspaceRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("schema_version = 0");
        builder.AppendLine();
        AppendToml(builder, "registry_id", request.RegistryId);
        AppendToml(builder, "display_name", request.DisplayName);
        if (request.IsProduction)
        {
            AppendToml(builder, "publisher_id", request.PublisherId!);
            AppendToml(builder, "signing_key_id", request.SigningKeyId!);
        }

        return builder.ToString();
    }

    private static string BuildGitIgnore(RegistryWorkspaceRequest request) =>
        "dist/\n" +
        "*.tmp\n" +
        (request.IsProduction
            ? "*.pem\n*.key\nprivate-keys/\n"
            : string.Empty);

    private static string BuildReadme(RegistryWorkspaceRequest request) =>
        $"# {request.DisplayName}\n\n" +
        $"Registry ID: `{request.RegistryId}`\n\n" +
        "Authoring packages live under " +
        "`templates/<namespace>/<package>/variants/<variant>`. Reusable " +
        "modules live under `modules/<namespace>/<module>`.\n\n" +
        "Use Klonker's Registry workspace window to validate, build, and " +
        (request.IsProduction
            ? "sign `dist/registry.json`."
            : "register `dist/registry.json` as a local development source.");

    private static string BuildDevelopmentGuide(
        RegistryWorkspaceRequest request) =>
        "# Template development\n\n" +
        "1. Create or import a package with Klonker's Template wizard.\n" +
        "2. Place it under `templates/<namespace>/<package>`.\n" +
        "3. Add reusable modules under `modules/<namespace>/<module>` with " +
        "`module.toml` and `content/`.\n" +
        "4. Open this workspace in the Registry wizard.\n" +
        "5. Validate and build. Klonker rebuilds `dist` transactionally.\n" +
        "6. For development registries, register the generated local index " +
        "and refresh the catalog.\n\n" +
        "Template content is data only. Registry builds never execute package scripts.";

    private static string BuildPublishingGuide(
        RegistryWorkspaceRequest request) =>
        "# Publishing and key rotation\n\n" +
        $"Publisher: `{request.PublisherId}`\n\n" +
        $"Active key: `{request.SigningKeyId}`\n\n" +
        $"Repository public key: `keys/{request.SigningKeyId}.spki`\n\n" +
        "Keep the PKCS#8 private PEM outside this workspace and store its " +
        "contents in your CI secret manager. Build and sign from Klonker, " +
        "then publish the complete `dist` directory. For rotation, create a " +
        "new key ID, publish and trust its public key before revoking the old key.";

    private static PlannedFile TextFile(string path, string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        return new PlannedFile(
            path,
            Utf8.GetBytes(normalized).ToImmutableArray(),
            IsText: true,
            normalized,
            "[registry wizard]");
    }

    private static void AddParents(
        string relativePath,
        HashSet<string> directories)
    {
        var parts = relativePath.Split('/');
        for (var length = 1; length < parts.Length; length++)
        {
            directories.Add(string.Join('/', parts.Take(length)));
        }
    }

    private static void ValidateId(
        string value,
        string label,
        List<ValidationIssue> issues)
    {
        if (!IdPattern().IsMatch(value))
        {
            issues.Add(Error(
                $"registry.workspace_{label.Replace(' ', '_')}_id_invalid",
                $"{char.ToUpperInvariant(label[0])}{label[1..]} ID '{value}' must start with a lowercase letter and contain only lowercase letters, numbers, and hyphens."));
        }
    }

    private static void AppendToml(
        StringBuilder builder,
        string key,
        string value) =>
        builder.Append(key)
            .Append(" = \"")
            .Append(value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal))
            .AppendLine("\"");

    private static ValidationIssue Error(
        string code,
        string message,
        string? path = null) =>
        new(ValidationSeverity.Error, code, message, Path: path);

    [GeneratedRegex(@"\A[a-z][a-z0-9-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
