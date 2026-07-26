using Klonker.Core.Diagnostics;

namespace Klonker.Core.Generation;

public static class GenerationDestinationValidator
{
    public static OperationResult<string> Validate(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return Failure(
                "destination.required",
                "Choose or enter a destination directory.");
        }

        string destination;
        try
        {
            destination = Path.GetFullPath(destinationPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            return Failure(
                "destination.invalid",
                $"The destination path is invalid: {exception.Message}");
        }

        var parent = Directory.GetParent(destination);
        if (parent is null || !parent.Exists)
        {
            return Failure(
                "destination.parent_missing",
                "The destination's parent directory must already exist.");
        }

        if (File.Exists(destination))
        {
            return Failure(
                "destination.is_file",
                "The destination is an existing file.");
        }

        try
        {
            if (Directory.Exists(destination) &&
                Directory.EnumerateFileSystemEntries(destination).Any())
            {
                return Failure(
                    "destination.not_empty",
                    "The destination directory must be new or empty.");
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "destination.inspect_failed",
                $"The destination could not be inspected: {exception.Message}");
        }

        return new OperationResult<string>(destination, []);
    }

    private static OperationResult<string> Failure(string code, string message) =>
        new(
            null,
            [
                new ValidationIssue(
                    ValidationSeverity.Error,
                    code,
                    message),
            ]);
}
