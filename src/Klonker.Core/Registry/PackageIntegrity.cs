using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Klonker.Core.Diagnostics;

namespace Klonker.Core.Registry;

public static class PackageIntegrity
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static async Task<OperationResult<PackageArtifactInfo>> InspectAsync(
        string artifactPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactPath);

        var fullPath = Path.GetFullPath(artifactPath);
        try
        {
            if (File.Exists(fullPath))
            {
                return new OperationResult<PackageArtifactInfo>(
                    await InspectFileAsync(fullPath, cancellationToken)
                        .ConfigureAwait(false),
                    []);
            }

            if (Directory.Exists(fullPath))
            {
                return new OperationResult<PackageArtifactInfo>(
                    await InspectDirectoryAsync(fullPath, cancellationToken)
                        .ConfigureAwait(false),
                    []);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "registry.package_inspection_failed",
                $"The package artifact could not be inspected: {exception.Message}",
                fullPath);
        }

        return Failure(
            "registry.package_not_found",
            "The package artifact does not exist.",
            fullPath);
    }

    public static async Task<OperationResult<PackageArtifactInfo>> VerifyAsync(
        string artifactPath,
        RegistryTemplateEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var inspected = await InspectAsync(artifactPath, cancellationToken)
            .ConfigureAwait(false);
        if (!inspected.IsSuccess)
        {
            return inspected;
        }

        var issues = new List<ValidationIssue>();
        if (!string.Equals(
                inspected.Value!.Sha256,
                entry.PackageSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "registry.package_checksum_mismatch",
                $"Package '{entry.TemplateId}' does not match its registry SHA-256.",
                Path: entry.PackagePath));
        }

        if (inspected.Value.SizeBytes != entry.PackageSizeBytes)
        {
            issues.Add(new ValidationIssue(
                ValidationSeverity.Error,
                "registry.package_size_mismatch",
                $"Package '{entry.TemplateId}' does not match its registry size.",
                Path: entry.PackagePath));
        }

        return new OperationResult<PackageArtifactInfo>(
            issues.Count == 0 ? inspected.Value : null,
            issues);
    }

    private static async Task<PackageArtifactInfo> InspectFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken)
            .ConfigureAwait(false);
        return new PackageArtifactInfo(
            Convert.ToHexStringLower(hash),
            stream.Length);
    }

    private static async Task<PackageArtifactInfo> InspectDirectoryAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var files = EnumerateDirectoryFiles(root)
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long totalBytes = 0;
        var lengthBytes = new byte[sizeof(long)];

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pathBytes = StrictUtf8.GetBytes(file.RelativePath);
            hash.AppendData(pathBytes);
            hash.AppendData([0]);

            BinaryPrimitives.WriteInt64BigEndian(lengthBytes, file.Length);
            hash.AppendData(lengthBytes);
            hash.AppendData([0]);

            await using var stream = new FileStream(
                file.FullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);
            var buffer = new byte[81920];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                hash.AppendData(buffer.AsSpan(0, read));
            }

            hash.AppendData([0xFF]);
            totalBytes = checked(totalBytes + file.Length);
        }

        return new PackageArtifactInfo(
            Convert.ToHexStringLower(hash.GetHashAndReset()),
            totalBytes);
    }

    private static IEnumerable<PackageFile> EnumerateDirectoryFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            var directoryInfo = new DirectoryInfo(directory);
            if (directoryInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException(
                    "Template package directories cannot be symbolic links or reparse points.");
            }

            foreach (var childDirectory in Directory
                         .EnumerateDirectories(directory)
                         .OrderDescending(StringComparer.Ordinal))
            {
                pending.Push(childDirectory);
            }

            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var info = new FileInfo(file);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new IOException(
                        "Template package files cannot be symbolic links or reparse points.");
                }

                yield return new PackageFile(
                    info.FullName,
                    Path.GetRelativePath(root, info.FullName).Replace('\\', '/'),
                    info.Length);
            }
        }
    }

    private static OperationResult<PackageArtifactInfo> Failure(
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

    private sealed record PackageFile(
        string FullPath,
        string RelativePath,
        long Length);
}
