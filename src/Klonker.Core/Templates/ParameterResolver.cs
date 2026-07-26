using Klonker.Core.Diagnostics;

namespace Klonker.Core.Templates;

public static class ParameterResolver
{
    public static OperationResult<ResolvedParameters> Resolve(
        TemplateManifest manifest,
        IReadOnlyDictionary<string, object?>? suppliedValues)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        suppliedValues ??= new Dictionary<string, object?>();
        var issues = new List<ValidationIssue>();
        var resolved = new Dictionary<string, object>(StringComparer.Ordinal);
        var declaredIds = manifest.Parameters
            .Select(parameter => parameter.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var suppliedId in suppliedValues.Keys.Order(StringComparer.Ordinal))
        {
            if (!declaredIds.Contains(suppliedId))
            {
                issues.Add(Error(
                    "parameter.unknown",
                    $"Value was supplied for undeclared parameter '{suppliedId}'.",
                    suppliedId));
            }
        }

        foreach (var parameter in manifest.Parameters)
        {
            var hasValue = suppliedValues.TryGetValue(parameter.Id, out var value);
            if ((!hasValue || value is null) && parameter.DefaultValue is not null)
            {
                value = parameter.DefaultValue;
                hasValue = true;
            }

            if (!hasValue || value is null)
            {
                if (parameter.Required)
                {
                    issues.Add(Error(
                        "parameter.required",
                        $"'{parameter.Label}' is required.",
                        parameter.Id));
                }

                continue;
            }

            object? normalizedValue = parameter.Type switch
            {
                TemplateParameterType.Text => ResolveString(parameter, value, issues),
                TemplateParameterType.Boolean => ResolveBoolean(parameter, value, issues),
                TemplateParameterType.Choice => ResolveChoice(parameter, value, issues),
                _ => null,
            };

            if (normalizedValue is not null)
            {
                resolved.Add(parameter.Id, normalizedValue);
            }
        }

        if (issues.Any(issue => issue.Severity == ValidationSeverity.Error))
        {
            return new OperationResult<ResolvedParameters>(null, issues);
        }

        return new OperationResult<ResolvedParameters>(
            new ResolvedParameters(resolved),
            issues);
    }

    private static string? ResolveString(
        TemplateParameterDefinition parameter,
        object value,
        List<ValidationIssue> issues)
    {
        if (value is not string text)
        {
            issues.Add(Error(
                "parameter.type",
                $"'{parameter.Label}' must be text.",
                parameter.Id));
            return null;
        }

        if (parameter.Required && string.IsNullOrWhiteSpace(text))
        {
            issues.Add(Error(
                "parameter.required",
                $"'{parameter.Label}' is required.",
                parameter.Id));
            return null;
        }

        if (parameter.Validation == "cpp_identifier" &&
            !StringHelpers.IsValidCppIdentifier(text))
        {
            issues.Add(Error(
                "parameter.cpp_identifier",
                $"'{parameter.Label}' must be a valid C++ identifier and cannot be a C++ keyword.",
                parameter.Id));
            return null;
        }

        return text;
    }

    private static bool? ResolveBoolean(
        TemplateParameterDefinition parameter,
        object value,
        List<ValidationIssue> issues)
    {
        if (value is bool boolean)
        {
            return boolean;
        }

        if (value is string text && bool.TryParse(text, out var parsed))
        {
            return parsed;
        }

        issues.Add(Error(
            "parameter.type",
            $"'{parameter.Label}' must be true or false.",
            parameter.Id));
        return null;
    }

    private static string? ResolveChoice(
        TemplateParameterDefinition parameter,
        object value,
        List<ValidationIssue> issues)
    {
        if (value is not string selected)
        {
            issues.Add(Error(
                "parameter.type",
                $"'{parameter.Label}' must be one of its declared choices.",
                parameter.Id));
            return null;
        }

        if (!parameter.Values.Contains(selected, StringComparer.Ordinal))
        {
            issues.Add(Error(
                "parameter.choice",
                $"'{selected}' is not an allowed value for '{parameter.Label}'.",
                parameter.Id));
            return null;
        }

        return selected;
    }

    private static ValidationIssue Error(string code, string message, string parameterId) =>
        new(ValidationSeverity.Error, code, message, parameterId);
}
