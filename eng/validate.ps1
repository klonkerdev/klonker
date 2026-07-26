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

    Write-Host 'Klonker validation succeeded.'
}
finally {
    Pop-Location
}
