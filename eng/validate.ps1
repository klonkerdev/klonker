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

    $samplePackage = Join-Path -Path $repositoryRoot -ChildPath (
        'samples/local-registry/packages/std.cpp-cli.windows-cmake')
    $sampleRegistryPath = Join-Path -Path $repositoryRoot -ChildPath (
        'samples/local-registry/registry.json')
    $integrityScript = Join-Path -Path $PSScriptRoot -ChildPath (
        'get-package-integrity.ps1')
    $integrity = & $integrityScript -PackageRoot $samplePackage
    $sampleRegistry = Get-Content -LiteralPath $sampleRegistryPath -Raw |
        ConvertFrom-Json
    $entry = @($sampleRegistry.templates) |
        Where-Object {
            $_.template_id -eq 'std.cpp-cli.windows-cmake'
        } |
        Select-Object -First 1
    if ($null -eq $entry) {
        throw 'The development sample registry entry is missing.'
    }

    if (
        $entry.package_sha256 -ne $integrity.package_sha256 -or
        [int64] $entry.package_size_bytes -ne
            [int64] $integrity.package_size_bytes
    ) {
        throw (
            'The development sample registry checksum/size is stale. ' +
            'Run eng/get-package-integrity.ps1 and update registry.json.')
    }

    Write-Host 'Klonker validation succeeded.'
}
finally {
    Pop-Location
}
