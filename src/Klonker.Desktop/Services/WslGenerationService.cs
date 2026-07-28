using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Klonker.Core.Diagnostics;
using Klonker.Core.Generation;
using Klonker.Core.Modules;
using Klonker.Core.Paths;

namespace Klonker.Desktop.Services;

public sealed class WslGenerationService : IWslGenerationService
{
    private const int MaximumCommandOutputBytes = 64 * 1024;

    public async Task<OperationResult<WslDistributionSnapshot>> DiscoverRunningAsync(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Failure<WslDistributionSnapshot>(
                "wsl.platform_unsupported",
                "WSL generation is available only when Klonker is running on Windows.");
        }

        var listed = await RunWslAsync(
            ["--list", "--running", "--quiet"],
            cancellationToken).ConfigureAwait(false);
        if (!listed.IsSuccess)
        {
            return new OperationResult<WslDistributionSnapshot>(
                null,
                listed.Issues);
        }

        var names = ParseDistributionNames(listed.Value!.StandardOutput);
        var distributions = ImmutableArray.CreateBuilder<WslDistribution>();
        var issues = new List<ValidationIssue>(listed.Issues);
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var home = await RunWslAsync(
                ["--distribution", name, "--exec", "printenv", "HOME"],
                cancellationToken).ConfigureAwait(false);
            if (!home.IsSuccess)
            {
                issues.AddRange(home.Issues.Select(issue =>
                    issue with
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Distribution '{name}': {issue.Message}",
                    }));
                continue;
            }

            var homePath = NormalizeLinuxOutput(home.Value!.StandardOutput);
            if (!TryValidateLinuxAbsolutePath(homePath, allowRoot: false, out _))
            {
                issues.Add(new ValidationIssue(
                    ValidationSeverity.Warning,
                    "wsl.home_invalid",
                    $"Distribution '{name}' returned an invalid HOME path and was skipped."));
                continue;
            }

            distributions.Add(new WslDistribution(name, homePath));
        }

        if (distributions.Count == 0)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Warning,
                "wsl.none_running",
                "No running WSL distributions were found. Start one, then refresh."));
        }

        return new OperationResult<WslDistributionSnapshot>(
            new WslDistributionSnapshot(
                distributions
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToImmutableArray()),
            issues);
    }

    public OperationResult<WslDestination> ResolveDestination(
        string distributionName,
        string linuxPath)
    {
        if (!TryValidateDistributionName(distributionName, out var nameMessage))
        {
            return Failure<WslDestination>(
                "wsl.distribution_invalid",
                nameMessage);
        }

        if (!TryValidateLinuxAbsolutePath(
                linuxPath,
                allowRoot: false,
                out var pathMessage))
        {
            return Failure<WslDestination>(
                "wsl.path_invalid",
                pathMessage);
        }

        var normalizedLinuxPath = NormalizeLinuxPath(linuxPath);
        var relative = normalizedLinuxPath.TrimStart('/');
        var safeRelative = SafePath.NormalizeRelative(relative);
        if (!safeRelative.IsSuccess)
        {
            return new OperationResult<WslDestination>(
                null,
                safeRelative.Issues.Select(issue =>
                    issue with
                    {
                        Code = "wsl.path_windows_incompatible",
                        Message =
                            "This Linux path cannot be accessed safely through the Windows WSL file provider: " +
                            issue.Message,
                    }));
        }

        var unc = $@"\\wsl.localhost\{distributionName}\" +
            safeRelative.Value!.Replace('/', '\\');
        return new OperationResult<WslDestination>(
            new WslDestination(
                distributionName,
                normalizedLinuxPath,
                unc),
            []);
    }

    public async Task<GenerationResult> GenerateProjectAsync(
        GenerationPlan plan,
        string distributionName,
        string linuxPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var ready = await EnsureRunningAndResolveAsync(
            distributionName,
            linuxPath,
            cancellationToken).ConfigureAwait(false);
        if (!ready.IsSuccess)
        {
            return Rejected(
                "The selected WSL generation target is unavailable.",
                ready.Issues);
        }

        var generated = await GenerationExecutor.ExecuteAsync(
            plan,
            ready.Value!.WindowsUncPath,
            cancellationToken).ConfigureAwait(false);
        if (!generated.Succeeded)
        {
            return generated;
        }

        return await VerifyAsync(
            plan.Files,
            ready.Value,
            generated.Message,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GenerationResult> GenerateModuleAsync(
        ModuleGenerationPlan plan,
        string distributionName,
        string linuxPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var ready = await EnsureRunningAndResolveAsync(
            distributionName,
            linuxPath,
            cancellationToken).ConfigureAwait(false);
        if (!ready.IsSuccess)
        {
            return Rejected(
                "The selected WSL generation target is unavailable.",
                ready.Issues);
        }

        var generated = await ModuleGenerationExecutor.ExecuteAsync(
            plan,
            ready.Value!.WindowsUncPath,
            cancellationToken).ConfigureAwait(false);
        if (!generated.Succeeded)
        {
            return generated;
        }

        return await VerifyAsync(
            plan.FilePlan.Files,
            ready.Value,
            generated.Message,
            cancellationToken).ConfigureAwait(false);
    }

    public static ImmutableArray<string> ParseDistributionNames(byte[] bytes)
    {
        var text = DecodeWslOutput(bytes);
        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Replace("\0", string.Empty, StringComparison.Ordinal).Trim())
            .Where(line => TryValidateDistributionName(line, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    internal static string DecodeWslOutput(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        var oddNulls = 0;
        for (var index = 1; index < bytes.Length; index += 2)
        {
            if (bytes[index] == 0)
            {
                oddNulls++;
            }
        }

        return oddNulls > bytes.Length / 8
            ? Encoding.Unicode.GetString(bytes)
            : new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)
                .GetString(bytes);
    }

    private async Task<OperationResult<WslDestination>> EnsureRunningAndResolveAsync(
        string distributionName,
        string linuxPath,
        CancellationToken cancellationToken)
    {
        var destination = ResolveDestination(distributionName, linuxPath);
        if (!destination.IsSuccess)
        {
            return destination;
        }

        var running = await DiscoverRunningAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!running.IsSuccess)
        {
            return new OperationResult<WslDestination>(null, running.Issues);
        }

        if (!running.Value!.Distributions.Any(item =>
                string.Equals(
                    item.Name,
                    distributionName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return Failure<WslDestination>(
                "wsl.distribution_not_running",
                $"WSL distribution '{distributionName}' is not running. Start it and refresh the list before generating.");
        }

        return destination;
    }

    private static async Task<GenerationResult> VerifyAsync(
        ImmutableArray<PlannedFile> files,
        WslDestination destination,
        string successMessage,
        CancellationToken cancellationToken)
    {
        var directoryCheck = await RunWslAsync(
            [
                "--distribution",
                destination.DistributionName,
                "--exec",
                "test",
                "-d",
                destination.LinuxPath,
            ],
            cancellationToken).ConfigureAwait(false);
        if (!directoryCheck.IsSuccess)
        {
            return new GenerationResult(
                GenerationStatus.Failed,
                "The files were transferred, but the selected WSL distribution could not confirm the destination directory.",
                directoryCheck.Issues);
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = SafePath.ResolveUnderRoot(
                destination.WindowsUncPath,
                file.RelativePath);
            if (!resolved.IsSuccess)
            {
                return new GenerationResult(
                    GenerationStatus.Failed,
                    "A generated WSL file could not be resolved for verification.",
                    resolved.Issues);
            }

            try
            {
                var actual = await File.ReadAllBytesAsync(
                        resolved.Value!,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!actual.AsSpan().SequenceEqual(file.Content.AsSpan()))
                {
                    return new GenerationResult(
                        GenerationStatus.Failed,
                        $"Generated WSL file '{file.RelativePath}' did not match the preview.",
                        [
                            new ValidationIssue(
                                ValidationSeverity.Error,
                                "wsl.verification_mismatch",
                                "The file read back from WSL did not match the planned bytes.",
                                Path: file.RelativePath),
                        ]);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return new GenerationResult(
                    GenerationStatus.Failed,
                    $"Generated WSL file '{file.RelativePath}' could not be read back.",
                    [],
                    exception);
            }
        }

        return new GenerationResult(
            GenerationStatus.Succeeded,
            $"{successMessage} WSL confirmed the directory and Klonker read back all {files.Length} files.",
            []);
    }

    private static async Task<OperationResult<WslCommandResult>> RunWslAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return Failure<WslCommandResult>(
                    "wsl.start_failed",
                    "Windows could not start wsl.exe.");
            }

            var outputTask = ReadLimitedAsync(
                process.StandardOutput.BaseStream,
                cancellationToken);
            var errorTask = ReadLimitedAsync(
                process.StandardError.BaseStream,
                cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                var detail = NormalizeLinuxOutput(error);
                return Failure<WslCommandResult>(
                    "wsl.command_failed",
                    detail.Length == 0
                        ? $"wsl.exe exited with code {process.ExitCode}."
                        : $"wsl.exe exited with code {process.ExitCode}: {detail}");
            }

            return new OperationResult<WslCommandResult>(
                new WslCommandResult(output, error),
                []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
                InvalidOperationException or
                System.ComponentModel.Win32Exception)
        {
            return Failure<WslCommandResult>(
                "wsl.unavailable",
                $"WSL could not be queried: {exception.Message}");
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        await using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (output.Length + read > MaximumCommandOutputBytes)
            {
                throw new IOException("wsl.exe returned more output than Klonker allows.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static bool TryValidateDistributionName(
        string? name,
        out string message)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Length > 128 ||
            name is "." or ".." ||
            name.Any(character =>
                char.IsControl(character) ||
                character is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|'))
        {
            message = "Select a valid running WSL distribution.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static bool TryValidateLinuxAbsolutePath(
        string? path,
        bool allowRoot,
        out string message)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path[0] != '/' ||
            path.Contains('\0'))
        {
            message = "Enter an absolute Linux path beginning with '/'.";
            return false;
        }

        var normalized = NormalizeLinuxPath(path);
        if (!allowRoot && normalized == "/")
        {
            message = "Generation directly into the Linux filesystem root is not allowed.";
            return false;
        }

        if (normalized.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries).Any(segment =>
                segment is "." or ".."))
        {
            message = "Linux destination paths cannot contain '.' or '..' traversal segments.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static string NormalizeLinuxPath(string path)
    {
        var segments = path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return "/" + string.Join('/', segments);
    }

    private static string NormalizeLinuxOutput(byte[] bytes) =>
        DecodeWslOutput(bytes)
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Trim();

    private static OperationResult<T> Failure<T>(
        string code,
        string message)
        where T : class =>
        new(
            null,
            [new ValidationIssue(ValidationSeverity.Error, code, message)]);

    private static GenerationResult Rejected(
        string message,
        IEnumerable<ValidationIssue> issues) =>
        new(GenerationStatus.Rejected, message, issues.ToImmutableArray());

    private sealed record WslCommandResult(
        byte[] StandardOutput,
        byte[] StandardError);
}
