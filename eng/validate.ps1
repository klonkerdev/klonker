[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path -Path $PSScriptRoot -ChildPath '..'))
$solution = Join-Path -Path $repositoryRoot -ChildPath 'Klonker.slnx'

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]] $CommandArguments
    )

    & dotnet @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($CommandArguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-LocalRegistryIntegrity {
    param(
        [Parameter(Mandatory)]
        [string] $RegistryPath,

        [Parameter(Mandatory)]
        [string] $IntegrityScript
    )

    $registryRoot = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::GetDirectoryName($RegistryPath))
    $registry = Get-Content -LiteralPath $RegistryPath -Raw |
        ConvertFrom-Json
    if ($registry.schema_version -ne 1) {
        throw "Sample registry '$RegistryPath' must use schema version 1."
    }

    $templateEntries = @($registry.templates)
    $moduleEntries = if (
        $null -ne $registry.PSObject.Properties['modules']
    ) {
        @($registry.modules)
    }
    else {
        @()
    }
    $entries = @($templateEntries) + @($moduleEntries)
    if ($entries.Count -eq 0) {
        throw "Sample registry '$RegistryPath' contains no templates or modules."
    }

    $identities = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $resolvedPackages = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $entries) {
        $artifactId = if (
            $null -ne $entry.PSObject.Properties['template_id']
        ) {
            [string] $entry.template_id
        }
        elseif ($null -ne $entry.PSObject.Properties['module_id']) {
            [string] $entry.module_id
        }
        else {
            ''
        }
        $identity = "$artifactId@$($entry.version)"
        if (
            [string]::IsNullOrWhiteSpace($artifactId) -or
            -not $identities.Add($identity)
        ) {
            throw "Sample registry '$RegistryPath' contains invalid or duplicate identity '$identity'."
        }

        $relativePackagePath = [string] $entry.package_path
        if (
            [string]::IsNullOrWhiteSpace($relativePackagePath) -or
            [System.IO.Path]::IsPathRooted($relativePackagePath) -or
            $relativePackagePath -match '^[A-Za-z]:' -or
            @($relativePackagePath -split '[/\\]') -contains '..'
        ) {
            throw "Sample registry '$RegistryPath' contains unsafe package path '$relativePackagePath'."
        }

        $packagePath = [System.IO.Path]::GetFullPath(
            (Join-Path -Path $registryRoot -ChildPath $relativePackagePath))
        $relativeToRegistry = [System.IO.Path]::GetRelativePath(
            $registryRoot,
            $packagePath)
        if (
            [System.IO.Path]::IsPathRooted($relativeToRegistry) -or
            $relativeToRegistry -eq '..' -or
            $relativeToRegistry.StartsWith(
                "..$([System.IO.Path]::DirectorySeparatorChar)",
                [System.StringComparison]::Ordinal)
        ) {
            throw "Sample package '$relativePackagePath' resolves outside '$registryRoot'."
        }

        if (Test-Path -LiteralPath $packagePath -PathType Container) {
            $integrity = & $IntegrityScript -PackageRoot $packagePath
        }
        elseif (Test-Path -LiteralPath $packagePath -PathType Leaf) {
            $file = Get-Item -LiteralPath $packagePath
            $integrity = [pscustomobject] @{
                package_sha256 = (
                    Get-FileHash -LiteralPath $packagePath -Algorithm SHA256
                ).Hash.ToLowerInvariant()
                package_size_bytes = [int64] $file.Length
            }
        }
        else {
            throw "Sample registry package '$packagePath' does not exist."
        }

        if (
            [string] $entry.package_sha256 -cne $integrity.package_sha256 -or
            [int64] $entry.package_size_bytes -ne
                [int64] $integrity.package_size_bytes
        ) {
            throw (
                "Sample registry entry '$identity' has stale checksum/size metadata. " +
                'Regenerate its integrity values.')
        }

        $resolvedPackages.Add($packagePath) | Out-Null
    }

    $packageManifests = @(
        Get-ChildItem -LiteralPath $registryRoot -Filter 'template.toml' -File -Recurse)
    foreach ($manifest in $packageManifests) {
        if (-not $resolvedPackages.Contains($manifest.DirectoryName)) {
            throw "Sample package '$($manifest.DirectoryName)' is not referenced by '$RegistryPath'."
        }

        if (
            Select-String `
                -LiteralPath $manifest.FullName `
                -Pattern '(?m)^\s*favorite\s*=' `
                -Quiet
        ) {
            throw "Template manifest '$($manifest.FullName)' contains app-local favorite state."
        }
    }

    $moduleManifests = @(
        Get-ChildItem -LiteralPath $registryRoot -Filter 'module.toml' -File -Recurse)
    foreach ($manifest in $moduleManifests) {
        if (-not $resolvedPackages.Contains($manifest.DirectoryName)) {
            throw "Sample module '$($manifest.DirectoryName)' is not referenced by '$RegistryPath'."
        }

        if (
            Select-String `
                -LiteralPath $manifest.FullName `
                -Pattern '(?m)^\s*favorite\s*=' `
                -Quiet
        ) {
            throw "Module manifest '$($manifest.FullName)' contains app-local favorite state."
        }
    }
}

Push-Location -LiteralPath $repositoryRoot
try {
    Invoke-DotNet -CommandArguments @('restore', $solution)
    Invoke-DotNet -CommandArguments @(
        'build',
        $solution,
        '--configuration',
        'Debug',
        '--no-restore')
    Invoke-DotNet -CommandArguments @(
        'test',
        $solution,
        '--configuration',
        'Debug',
        '--no-build',
        '--no-restore')
    Invoke-DotNet -CommandArguments @(
        'format',
        $solution,
        '--verify-no-changes',
        '--no-restore')

    $integrityScript = Join-Path -Path $PSScriptRoot -ChildPath (
        'get-package-integrity.ps1')
    $sampleRegistryPaths = @(
        Get-ChildItem `
            -LiteralPath (Join-Path -Path $repositoryRoot -ChildPath 'samples') `
            -Filter 'registry.json' `
            -File `
            -Recurse |
            Sort-Object FullName)
    if ($sampleRegistryPaths.Count -eq 0) {
        throw 'No sample registry indexes were discovered.'
    }

    foreach ($sampleRegistry in $sampleRegistryPaths) {
        Assert-LocalRegistryIntegrity `
            -RegistryPath $sampleRegistry.FullName `
            -IntegrityScript $integrityScript
    }

    Write-Host 'Klonker validation succeeded.'
}
finally {
    Pop-Location
}
