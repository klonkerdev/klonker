[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments)]
    [string[]] $ApplicationArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path -Path $PSScriptRoot -ChildPath '..'))
$desktopProject = Join-Path -Path $repositoryRoot -ChildPath (
    'src/Klonker.Desktop/Klonker.Desktop.csproj')

if (-not (Test-Path -LiteralPath $desktopProject -PathType Leaf)) {
    throw "Klonker.Desktop was not found at '$desktopProject'."
}

Push-Location -LiteralPath $repositoryRoot
try {
    & dotnet run --project $desktopProject -- @ApplicationArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Klonker.Desktop exited with code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
