using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;

namespace Klonker.Core.Authoring;

public static class TemplateAuthoringDestinationValidator
{
    public static OperationResult<string> Validate(
        string destinationPath,
        string? inspectedSourcePath = null)
    {
        var destination = GenerationDestinationValidator.Validate(
            destinationPath);
        if (!destination.IsSuccess ||
            string.IsNullOrWhiteSpace(inspectedSourcePath))
        {
            return destination;
        }

        string source;
        try
        {
            source = Path.GetFullPath(inspectedSourcePath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            return Failure(
                "authoring.source_invalid",
                $"The inspected source path is invalid: {exception.Message}");
        }

        if (IsSameOrChildPath(source, destination.Value!))
        {
            return Failure(
                "authoring.destination_inside_source",
                "Choose a destination outside the existing source folder so conversion cannot modify the original tree.");
        }

        return destination;
    }

    private static bool IsSameOrChildPath(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative == "." ||
            (!Path.IsPathRooted(relative) &&
             relative != ".." &&
             !relative.StartsWith(
                 $"..{Path.DirectorySeparatorChar}",
                 StringComparison.Ordinal) &&
             !relative.StartsWith(
                 $"..{Path.AltDirectorySeparatorChar}",
                 StringComparison.Ordinal));
    }

    private static OperationResult<string> Failure(
        string code,
        string message) =>
        new(
            null,
            [new ValidationIssue(ValidationSeverity.Error, code, message)]);
}
