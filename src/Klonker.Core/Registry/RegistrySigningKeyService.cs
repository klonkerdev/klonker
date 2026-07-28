using System.Security.Cryptography;
using System.Text;
using Klonker.Core.Diagnostics;

namespace Klonker.Core.Registry;

public static class RegistrySigningKeyService
{
    public static RegistrySigningKeyMaterial Create()
    {
        using var rsa = RSA.Create(3072);
        return new RegistrySigningKeyMaterial(
            Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()),
            rsa.ExportPkcs8PrivateKeyPem());
    }

    public static async Task<OperationResult<string>> WritePrivateKeyAsync(
        string destinationPath,
        string privateKeyPem,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(destinationPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            return Failure(
                "registry.signing_key_path_invalid",
                $"The private-key path is invalid: {exception.Message}",
                destinationPath);
        }

        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            return Failure(
                "registry.signing_key_exists",
                "The private-key destination already exists. Choose a new path so Klonker never overwrites key material.",
                fullPath);
        }

        try
        {
            var parent = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(parent))
            {
                return Failure(
                    "registry.signing_key_parent_invalid",
                    "The private-key destination must have a parent folder.",
                    fullPath);
            }

            Directory.CreateDirectory(parent);
            var temporaryPath = Path.Combine(
                parent,
                $".{Path.GetFileName(fullPath)}-{Guid.NewGuid():N}.tmp");
            var bytes = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false)
                .GetBytes(privateKeyPem.Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal));
            try
            {
                await using (var output = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 81920,
                                 useAsync: true))
                {
                    await output.WriteAsync(bytes, cancellationToken)
                        .ConfigureAwait(false);
                }

                File.Move(temporaryPath, fullPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return new OperationResult<string>(fullPath, []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "registry.signing_key_write_failed",
                $"The private key could not be written: {exception.Message}",
                fullPath);
        }
    }

    private static OperationResult<string> Failure(
        string code,
        string message,
        string path) =>
        new(
            null,
            [new ValidationIssue(
                ValidationSeverity.Error,
                code,
                message,
                Path: path)]);
}
