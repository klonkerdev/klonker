using System.Globalization;
using System.Text.RegularExpressions;
using Klonker.Core.Diagnostics;
using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Klonker.Core.Templates;

public static partial class RestrictedTemplateRenderer
{
    public static OperationResult<string> Render(
        string templateText,
        string sourceName,
        ResolvedParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(templateText);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(parameters);

        if (FunctionDeclarationPattern().IsMatch(templateText))
        {
            return Failure(
                "template.function_not_allowed",
                $"Template-defined functions are not allowed in '{sourceName}'.",
                sourceName);
        }

        var template = Template.Parse(templateText, sourceName);
        if (template.HasErrors)
        {
            var detail = string.Join("; ", template.Messages.Select(message => message.ToString()));
            return Failure(
                "template.parse",
                $"Template '{sourceName}' is malformed: {detail}",
                sourceName);
        }

        var builtins = new ScriptObject();
        builtins.Import("lower_case", (Func<string, string>)StringHelpers.LowerCase);
        builtins.Import("upper_case", (Func<string, string>)StringHelpers.UpperCase);
        builtins.Import("snake_case", (Func<string, string>)StringHelpers.SnakeCase);
        builtins.Import("kebab_case", (Func<string, string>)StringHelpers.KebabCase);
        builtins.Import("pascal_case", (Func<string, string>)StringHelpers.PascalCase);
        builtins.Import("cpp_identifier", (Func<string, string>)StringHelpers.CppIdentifier);

        var globals = new ScriptObject();
        foreach (var value in parameters.Values)
        {
            globals.SetValue(value.Key, value.Value, readOnly: true);
        }

        var context = new TemplateContext(builtins)
        {
            StrictVariables = true,
            EnableRelaxedMemberAccess = false,
            EnableRelaxedFunctionAccess = false,
            EnableRelaxedIndexerAccess = false,
            EnableRelaxedTargetAccess = false,
            MemberFilter = _ => false,
            TemplateLoader = null,
            NewLine = "\n",
            LoopLimit = 10_000,
            RecursiveLimit = 16,
        };
        context.PushCulture(CultureInfo.InvariantCulture);
        context.PushGlobal(globals);

        try
        {
            return new OperationResult<string>(template.Render(context), []);
        }
        catch (ScriptRuntimeException exception)
        {
            return Failure(
                "template.render",
                $"Template '{sourceName}' could not be rendered: {exception.Message}",
                sourceName);
        }
    }

    private static OperationResult<string> Failure(string code, string message, string path) =>
        new(
            null,
            [new ValidationIssue(ValidationSeverity.Error, code, message, Path: path)]);

    [GeneratedRegex(@"\{\{[-~]?\s*func\b", RegexOptions.CultureInvariant)]
    private static partial Regex FunctionDeclarationPattern();
}
