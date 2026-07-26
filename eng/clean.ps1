[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path -Path $PSScriptRoot -ChildPath '..'))

function Remove-RepositoryDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Category
    )

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $relative = [System.IO.Path]::GetRelativePath($repositoryRoot, $resolved)
    if (
        [System.IO.Path]::IsPathRooted($relative) -or
        $relative -eq '..' -or
        $relative.StartsWith(
            "..$([System.IO.Path]::DirectorySeparatorChar)",
            [System.StringComparison]::Ordinal) -or
        $relative.StartsWith(
            "..$([System.IO.Path]::AltDirectorySeparatorChar)",
            [System.StringComparison]::Ordinal)
    ) {
        throw "Refusing to remove '$resolved' because it is outside the repository."
    }

    if (Test-Path -LiteralPath $resolved -PathType Container) {
        Write-Host "Removing $Category`: $relative"
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

$projectRoots = @(
    (Join-Path -Path $repositoryRoot -ChildPath 'src'),
    (Join-Path -Path $repositoryRoot -ChildPath 'tests')
)

$buildDirectories = foreach ($projectRoot in $projectRoots) {
    if (Test-Path -LiteralPath $projectRoot -PathType Container) {
        Get-ChildItem -LiteralPath $projectRoot -Directory -Recurse |
            Where-Object { $_.Name -in @('bin', 'obj') }
    }
}

foreach ($directory in @($buildDirectories)) {
    Remove-RepositoryDirectory -Path $directory.FullName -Category 'build output'
}

$testResults = Get-ChildItem -LiteralPath $repositoryRoot -Directory -Recurse |
    Where-Object { $_.Name -eq 'TestResults' }
foreach ($directory in @($testResults)) {
    Remove-RepositoryDirectory -Path $directory.FullName -Category 'test results'
}

$sampleOutput = Join-Path -Path $repositoryRoot -ChildPath 'samples/generated-output'
Remove-RepositoryDirectory -Path $sampleOutput -Category 'generated sample output'

Write-Host 'Klonker repository-local output is clean.'
