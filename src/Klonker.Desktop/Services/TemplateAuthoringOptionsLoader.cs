using System.Text.Json;

namespace Klonker.Desktop.Services;

public static class TemplateAuthoringOptionsLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static TemplateAuthoringOptions LoadDefault()
    {
        using var stream = typeof(TemplateAuthoringOptionsLoader)
            .Assembly
            .GetManifestResourceStream(
                "Klonker.Desktop.Assets.template-authoring-options.json") ??
            throw new InvalidOperationException(
                "The embedded template authoring options are missing.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static TemplateAuthoringOptions Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        TemplateAuthoringOptions options;
        try
        {
            options = JsonSerializer.Deserialize<TemplateAuthoringOptions>(
                json,
                SerializerOptions) ??
                throw new InvalidOperationException(
                    "Template authoring options JSON did not contain a document.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Template authoring options JSON is invalid: {exception.Message}",
                exception);
        }

        Validate(options);
        return options;
    }

    private static void Validate(TemplateAuthoringOptions options)
    {
        if (options.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"Unsupported template authoring options schema {options.SchemaVersion}; expected 1.");
        }

        ValidateUniqueIds(options.Licenses.Select(option => option.Id), "license");
        ValidateUniqueIds(options.Platforms.Select(option => option.Id), "platform");
        ValidateUniqueIds(
            options.BuildSystems.Select(option => option.Id),
            "build system");
        ValidateUniqueIds(options.Languages.Select(option => option.Id), "language");

        if (options.Licenses.IsDefaultOrEmpty ||
            options.Platforms.IsDefaultOrEmpty ||
            options.BuildSystems.IsDefaultOrEmpty ||
            options.Languages.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException(
                "Template authoring options must define licenses, platforms, build systems, and languages.");
        }

        var buildSystemIds = options.BuildSystems
            .Select(option => option.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var language in options.Languages)
        {
            if (language.BuildSystems.IsDefaultOrEmpty)
            {
                throw new InvalidOperationException(
                    $"Language '{language.Id}' must allow at least one build system.");
            }

            foreach (var buildSystem in language.BuildSystems)
            {
                if (!buildSystemIds.Contains(buildSystem))
                {
                    throw new InvalidOperationException(
                        $"Language '{language.Id}' references unknown build system '{buildSystem}'.");
                }
            }
        }

        foreach (var seed in options.Languages
                     .SelectMany(language => language.SeedFiles)
                     .Concat(options.BuildSystems.SelectMany(
                         buildSystem => buildSystem.SeedFiles)))
        {
            if (string.IsNullOrWhiteSpace(seed.Path) ||
                string.IsNullOrWhiteSpace(seed.Content))
            {
                throw new InvalidOperationException(
                    "Every starter file requires a path and content.");
            }
        }
    }

    private static void ValidateUniqueIds(
        IEnumerable<string> ids,
        string description)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
            {
                throw new InvalidOperationException(
                    $"Template authoring options contain an empty or duplicate {description} ID '{id}'.");
            }
        }
    }
}
