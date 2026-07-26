[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$packageRootPath = [System.IO.Path]::GetFullPath($PackageRoot)
if (-not (Test-Path -LiteralPath $packageRootPath -PathType Container)) {
    throw "Package directory '$packageRootPath' does not exist."
}

$reparseEntries = Get-ChildItem -LiteralPath $packageRootPath -Recurse -Force |
    Where-Object {
        ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
    }
if (@($reparseEntries).Count -gt 0) {
    throw "Package '$packageRootPath' contains a symbolic link or reparse point."
}

$files = Get-ChildItem -LiteralPath $packageRootPath -File -Recurse -Force |
    Sort-Object {
        [System.IO.Path]::GetRelativePath($packageRootPath, $_.FullName).
            Replace('\', '/')
    }
$hash = [System.Security.Cryptography.IncrementalHash]::CreateHash(
    [System.Security.Cryptography.HashAlgorithmName]::SHA256)
$totalBytes = [int64] 0
try {
    foreach ($file in $files) {
        $relativePath = [System.IO.Path]::GetRelativePath(
            $packageRootPath,
            $file.FullName).Replace('\', '/')
        $hash.AppendData([System.Text.Encoding]::UTF8.GetBytes($relativePath))
        $hash.AppendData([byte[]] (0))

        $lengthBytes = [System.BitConverter]::GetBytes([int64] $file.Length)
        if ([System.BitConverter]::IsLittleEndian) {
            [System.Array]::Reverse($lengthBytes)
        }

        $hash.AppendData($lengthBytes)
        $hash.AppendData([byte[]] (0))
        $hash.AppendData([System.IO.File]::ReadAllBytes($file.FullName))
        $hash.AppendData([byte[]] (255))
        $totalBytes += $file.Length
    }

    [pscustomobject] @{
        package_sha256 = [System.Convert]::ToHexString(
            $hash.GetHashAndReset()).ToLowerInvariant()
        package_size_bytes = $totalBytes
        file_count = @($files).Count
    }
}
finally {
    $hash.Dispose()
}
