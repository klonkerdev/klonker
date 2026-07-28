[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SourceRoot,

    [Parameter(Mandatory)]
    [string] $OutputRoot,

    [string] $SigningKeyPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sourceRootPath = [System.IO.Path]::GetFullPath($SourceRoot)
$outputRootPath = [System.IO.Path]::GetFullPath($OutputRoot)
if (-not (Test-Path -LiteralPath $sourceRootPath -PathType Container)) {
    throw "Registry source root '$sourceRootPath' does not exist."
}

if (Test-Path -LiteralPath $outputRootPath) {
    throw "Registry output '$outputRootPath' already exists. Choose a new output directory."
}

$registryDefinitionPath = Join-Path -Path $sourceRootPath -ChildPath 'registry.toml'
$templatesRoot = Join-Path -Path $sourceRootPath -ChildPath 'templates'
$modulesRoot = Join-Path -Path $sourceRootPath -ChildPath 'modules'
if (-not (Test-Path -LiteralPath $registryDefinitionPath -PathType Leaf)) {
    throw "Registry definition '$registryDefinitionPath' does not exist."
}

if (
    -not (Test-Path -LiteralPath $templatesRoot -PathType Container) -and
    -not (Test-Path -LiteralPath $modulesRoot -PathType Container)
) {
    throw "Registry source must contain a 'templates' or 'modules' directory."
}

$outputParent = [System.IO.Directory]::GetParent($outputRootPath)
if ($null -eq $outputParent) {
    throw "Registry output '$outputRootPath' has no parent directory."
}

[System.IO.Directory]::CreateDirectory($outputParent.FullName) | Out-Null
$stagingRoot = Join-Path -Path $outputParent.FullName -ChildPath (
    ".$([System.IO.Path]::GetFileName($outputRootPath))-$([Guid]::NewGuid().ToString('N')).staging")
$stagingRoot = [System.IO.Path]::GetFullPath($stagingRoot)
$packagesOutput = Join-Path -Path $stagingRoot -ChildPath 'packages'

Add-Type -AssemblyName System.IO.Compression

$tomlStringJsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
$tomlStringJsonOptions.Encoder =
    [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping

function Normalize-LineEndings {
    param(
        [Parameter(Mandatory)]
        [string] $Text
    )

    return $Text.Replace("`r`n", "`n").Replace("`r", "`n")
}

function Get-TomlTopLevel {
    param(
        [Parameter(Mandatory)]
        [string] $Toml
    )

    $normalized = Normalize-LineEndings -Text $Toml
    $firstTable = [System.Text.RegularExpressions.Regex]::Match(
        $normalized,
        '(?m)^\s*\[\[')
    if ($firstTable.Success) {
        return $normalized.Substring(0, $firstTable.Index)
    }

    return $normalized
}

function Get-TomlTableText {
    param(
        [Parameter(Mandatory)]
        [string] $Toml,

        [Parameter(Mandatory)]
        [string[]] $AllowedTables,

        [Parameter(Mandatory)]
        [string] $Context
    )

    $normalized = Normalize-LineEndings -Text $Toml
    $matches = [System.Text.RegularExpressions.Regex]::Matches(
        $normalized,
        '(?m)^\s*\[\[([A-Za-z0-9_-]+)\]\]\s*(?:#.*)?$')
    foreach ($match in $matches) {
        $tableName = $match.Groups[1].Value
        if ($AllowedTables -cnotcontains $tableName) {
            throw "$Context contains unsupported table '[[$tableName]]'."
        }
    }

    $firstTable = [System.Text.RegularExpressions.Regex]::Match(
        $normalized,
        '(?m)^\s*\[\[')
    if ($firstTable.Success) {
        return $normalized.Substring($firstTable.Index).Trim()
    }

    return ''
}

function Get-TomlString {
    param(
        [Parameter(Mandatory)]
        [string] $Toml,

        [Parameter(Mandatory)]
        [string] $Property,

        [Parameter(Mandatory)]
        [string] $Context,

        [switch] $Optional
    )

    $topLevel = Get-TomlTopLevel -Toml $Toml
    $escapedProperty = [System.Text.RegularExpressions.Regex]::Escape($Property)
    $matches = [System.Text.RegularExpressions.Regex]::Matches(
        $topLevel,
        "(?m)^\s*$escapedProperty\s*=\s*(`"(?:\\.|[^`"\\])*`")\s*(?:#.*)?$")
    if ($matches.Count -eq 0 -and $Optional) {
        return $null
    }

    if ($matches.Count -ne 1) {
        throw "$Context must declare exactly one top-level '$Property' string."
    }

    try {
        return [System.Text.Json.JsonSerializer]::Deserialize(
            $matches[0].Groups[1].Value,
            [string])
    }
    catch [System.Text.Json.JsonException] {
        throw "$Context property '$Property' is not a supported TOML basic string."
    }
}

function Get-TomlInteger {
    param(
        [Parameter(Mandatory)]
        [string] $Toml,

        [Parameter(Mandatory)]
        [string] $Property,

        [Parameter(Mandatory)]
        [string] $Context
    )

    $topLevel = Get-TomlTopLevel -Toml $Toml
    $escapedProperty = [System.Text.RegularExpressions.Regex]::Escape($Property)
    $matches = [System.Text.RegularExpressions.Regex]::Matches(
        $topLevel,
        "(?m)^\s*$escapedProperty\s*=\s*(-?[0-9]+)\s*(?:#.*)?$")
    if ($matches.Count -ne 1) {
        throw "$Context must declare exactly one top-level '$Property' integer."
    }

    return [int]::Parse(
        $matches[0].Groups[1].Value,
        [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-TomlStringArray {
    param(
        [Parameter(Mandatory)]
        [string] $Toml,

        [Parameter(Mandatory)]
        [string] $Property,

        [Parameter(Mandatory)]
        [string] $Context,

        [switch] $Optional
    )

    $topLevel = Get-TomlTopLevel -Toml $Toml
    $escapedProperty = [System.Text.RegularExpressions.Regex]::Escape($Property)
    $matches = [System.Text.RegularExpressions.Regex]::Matches(
        $topLevel,
        "(?m)^\s*$escapedProperty\s*=\s*(\[[^\r\n]*\])\s*(?:#.*)?$")
    if ($matches.Count -eq 0 -and $Optional) {
        return [string[]] @()
    }

    if ($matches.Count -ne 1) {
        throw "$Context must declare exactly one top-level '$Property' string array."
    }

    try {
        $values = @($matches[0].Groups[1].Value | ConvertFrom-Json)
    }
    catch {
        throw "$Context property '$Property' is not a supported one-line string array."
    }

    if (@($values | Where-Object { $_ -isnot [string] }).Count -gt 0) {
        throw "$Context property '$Property' must contain only strings."
    }

    return [string[]] $values
}

function ConvertTo-TomlString {
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    return [System.Text.Json.JsonSerializer]::Serialize(
        $Value,
        [string],
        $tomlStringJsonOptions)
}

function ConvertTo-TomlStringArray {
    param(
        [Parameter(Mandatory)]
        [string[]] $Values
    )

    $encoded = @($Values | ForEach-Object { ConvertTo-TomlString -Value $_ })
    return "[$([string]::Join(', ', $encoded))]"
}

function ConvertTo-MarkdownCell {
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    return $Value.Replace('|', '\|').Replace("`r", ' ').Replace("`n", ' ')
}

function New-CatalogMarkdown {
    param(
        [Parameter(Mandatory)]
        [string] $RegistryDisplayName,

        [Parameter(Mandatory)]
        [object[]] $Packages,

        [object[]] $Modules = @()
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("# $(ConvertTo-MarkdownCell -Value $RegistryDisplayName) catalog")
    $lines.Add('')
    $lines.Add('<!-- Generated by eng/pack-registry.ps1. Do not edit by hand. -->')
    $lines.Add('')
    $lines.Add(
        'This catalog is generated from every discovered package and variant manifest. ' +
        'The adjacent `catalog.json` contains the same data for static sites and other tooling.')
    $lines.Add('')
    $lines.Add('| Package | Variant | Version | Target | Build system | Language | License | Description |')
    $lines.Add('| --- | --- | --- | --- | --- | --- | --- | --- | --- |')
    foreach ($package in $Packages) {
        $packageName = ConvertTo-MarkdownCell -Value ([string] $package.name)
        if (-not [string]::IsNullOrWhiteSpace(
                [string] $package.documentation_path
            )) {
            $documentationLink = "../$([string] $package.documentation_path)"
            $packageName = "[$packageName]($documentationLink)"
        }

        foreach ($variant in @($package.variants)) {
            $cells = @(
                $packageName,
                (ConvertTo-MarkdownCell -Value ([string] $variant.id)),
                (ConvertTo-MarkdownCell -Value ([string] $variant.version)),
                (ConvertTo-MarkdownCell -Value ([string] $variant.target_os)),
                (ConvertTo-MarkdownCell -Value ([string] $variant.build_system)),
                (ConvertTo-MarkdownCell -Value ([string] $package.language)),
                (ConvertTo-MarkdownCell -Value ([string] $package.source_license)),
                (ConvertTo-MarkdownCell -Value ([string] $variant.description))
            )
            $lines.Add("| $([string]::Join(' | ', $cells)) |")
        }
    }

    $lines.Add('')
    if ($Modules.Count -gt 0) {
        $lines.Add('## Modules')
        $lines.Add('')
        $lines.Add('| Module | Version | Language | License | Description |')
        $lines.Add('| --- | --- | --- | --- | --- |')
        foreach ($module in $Modules) {
            $cells = @(
                (ConvertTo-MarkdownCell -Value ([string] $module.name)),
                (ConvertTo-MarkdownCell -Value ([string] $module.version)),
                (ConvertTo-MarkdownCell -Value ([string] $module.language)),
                (ConvertTo-MarkdownCell -Value ([string] $module.source_license)),
                (ConvertTo-MarkdownCell -Value ([string] $module.description))
            )
            $lines.Add("| $([string]::Join(' | ', $cells)) |")
        }

        $lines.Add('')
    }

    return "$([string]::Join("`n", $lines))`n"
}

function Assert-Identifier {
    param(
        [Parameter(Mandatory)]
        [string] $Value,

        [Parameter(Mandatory)]
        [string] $Context
    )

    if ($Value -cnotmatch '^[a-z][a-z0-9-]*$') {
        throw "$Context '$Value' must start with a lowercase letter and contain only lowercase letters, numbers, and hyphens."
    }
}

function Assert-SafeArchivePath {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath,

        [Parameter(Mandatory)]
        [string] $Context
    )

    if (
        [string]::IsNullOrWhiteSpace($RelativePath) -or
        $RelativePath.IndexOf([char] 0) -ge 0 -or
        $RelativePath.Contains('\') -or
        $RelativePath.StartsWith('/', [System.StringComparison]::Ordinal) -or
        $RelativePath -match '^[A-Za-z]:' -or
        [System.IO.Path]::IsPathRooted($RelativePath)
    ) {
        throw "$Context contains unsafe path '$RelativePath'."
    }

    foreach ($segment in $RelativePath.Split('/')) {
        if (
            [string]::IsNullOrWhiteSpace($segment) -or
            $segment -eq '.' -or
            $segment -eq '..' -or
            $segment.IndexOfAny([char[]] '<>:"|?*') -ge 0 -or
            $segment.TrimEnd([char[]] @(' ', '.')) -cne $segment
        ) {
            throw "$Context contains unsafe path segment '$segment'."
        }

        $deviceName = $segment.Split('.')[0]
        if ($deviceName -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$') {
            throw "$Context contains reserved Windows device name '$segment'."
        }
    }
}

function Assert-NoReparsePoints {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $Context
    )

    $reparseEntries = Get-ChildItem -LiteralPath $Root -Recurse -Force |
        Where-Object {
            ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
        }
    if (@($reparseEntries).Count -gt 0) {
        throw "$Context contains a symbolic link or reparse point."
    }
}

function Add-ArchiveEntry {
    param(
        [Parameter(Mandatory)]
        [System.Collections.Generic.Dictionary[string, object]] $Entries,

        [Parameter(Mandatory)]
        [string] $RelativePath,

        [string] $SourcePath,

        [byte[]] $Content,

        [Parameter(Mandatory)]
        [string] $Context
    )

    Assert-SafeArchivePath -RelativePath $RelativePath -Context $Context
    if ($Entries.ContainsKey($RelativePath)) {
        throw "$Context contains duplicate or case-colliding path '$RelativePath'."
    }

    $Entries.Add(
        $RelativePath,
        [pscustomobject] @{
            RelativePath = $RelativePath
            SourcePath = $SourcePath
            Content = $Content
        })
}

function Add-SourceTree {
    param(
        [Parameter(Mandatory)]
        [System.Collections.Generic.Dictionary[string, object]] $Entries,

        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]] $ExcludedRelativePaths,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]] $ExcludedPrefixes,

        [Parameter(Mandatory)]
        [string] $Context
    )

    $files = Get-ChildItem -LiteralPath $Root -File -Recurse -Force |
        Sort-Object {
            [System.IO.Path]::GetRelativePath($Root, $_.FullName).Replace('\', '/')
        }
    foreach ($file in $files) {
        $relativePath = [System.IO.Path]::GetRelativePath(
            $Root,
            $file.FullName).Replace('\', '/')
        if ($ExcludedRelativePaths -ccontains $relativePath) {
            continue
        }

        $excluded = $false
        foreach ($prefix in $ExcludedPrefixes) {
            if ($relativePath.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
                $excluded = $true
                break
            }
        }

        if ($excluded) {
            continue
        }

        Add-ArchiveEntry `
            -Entries $Entries `
            -RelativePath $relativePath `
            -SourcePath $file.FullName `
            -Context $Context
    }
}

function Assert-NoFileDirectoryCollisions {
    param(
        [Parameter(Mandatory)]
        [System.Collections.Generic.Dictionary[string, object]] $Entries,

        [Parameter(Mandatory)]
        [string] $Context
    )

    foreach ($path in $Entries.Keys) {
        $segments = @($path.Split('/'))
        for ($count = 1; $count -lt $segments.Count; $count++) {
            $ancestor = [string]::Join('/', $segments[0..($count - 1)])
            if ($Entries.ContainsKey($ancestor)) {
                throw "$Context contains file/directory collision '$ancestor'."
            }
        }
    }
}

function New-DeterministicPackageArchive {
    param(
        [Parameter(Mandatory)]
        [System.Collections.Generic.Dictionary[string, object]] $Entries,

        [Parameter(Mandatory)]
        [string] $ArchivePath
    )

    $fileStream = [System.IO.File]::Open(
        $ArchivePath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $fileStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            foreach ($item in @($Entries.Values | Sort-Object RelativePath)) {
                $entry = $archive.CreateEntry(
                    $item.RelativePath,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(
                    1980,
                    1,
                    1,
                    0,
                    0,
                    0,
                    [TimeSpan]::Zero)
                $output = $entry.Open()
                try {
                    if (-not [string]::IsNullOrEmpty($item.SourcePath)) {
                        $input = [System.IO.File]::OpenRead($item.SourcePath)
                        try {
                            $input.CopyTo($output)
                        }
                        finally {
                            $input.Dispose()
                        }
                    }
                    else {
                        $output.Write($item.Content, 0, $item.Content.Length)
                    }
                }
                finally {
                    $output.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $fileStream.Dispose()
    }
}

function New-RuntimeManifest {
    param(
        [Parameter(Mandatory)]
        [string] $TemplateId,

        [Parameter(Mandatory)]
        [string] $FamilyId,

        [Parameter(Mandatory)]
        [string] $VariantId,

        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Description,

        [Parameter(Mandatory)]
        [string] $Version,

        [Parameter(Mandatory)]
        [string] $TargetOs,

        [Parameter(Mandatory)]
        [string] $BuildSystem,

        [Parameter(Mandatory)]
        [string] $Language,

        [Parameter(Mandatory)]
        [string] $SourceLicense,

        [string] $Logo,

        [Parameter(Mandatory)]
        [string[]] $Tags,

        [Parameter(Mandatory)]
        [string] $PackageTables,

        [Parameter(Mandatory)]
        [string] $VariantTables
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('schema_version = 0')
    $lines.Add('')
    $lines.Add("id = $(ConvertTo-TomlString -Value $TemplateId)")
    $lines.Add("family_id = $(ConvertTo-TomlString -Value $FamilyId)")
    $lines.Add("variant_id = $(ConvertTo-TomlString -Value $VariantId)")
    $lines.Add('')
    $lines.Add("name = $(ConvertTo-TomlString -Value $Name)")
    $lines.Add("description = $(ConvertTo-TomlString -Value $Description)")
    $lines.Add("version = $(ConvertTo-TomlString -Value $Version)")
    $lines.Add('')
    $lines.Add("target_os = $(ConvertTo-TomlString -Value $TargetOs)")
    $lines.Add("build_system = $(ConvertTo-TomlString -Value $BuildSystem)")
    $lines.Add("language = $(ConvertTo-TomlString -Value $Language)")
    $lines.Add('')
    $lines.Add("source_license = $(ConvertTo-TomlString -Value $SourceLicense)")
    if (-not [string]::IsNullOrWhiteSpace($Logo)) {
        $lines.Add("logo = $(ConvertTo-TomlString -Value $Logo)")
    }

    $lines.Add("tags = $(ConvertTo-TomlStringArray -Values $Tags)")
    if (-not [string]::IsNullOrWhiteSpace($PackageTables)) {
        $lines.Add('')
        $lines.Add($PackageTables)
    }

    if (-not [string]::IsNullOrWhiteSpace($VariantTables)) {
        $lines.Add('')
        $lines.Add($VariantTables)
    }

    return "$([string]::Join("`n", $lines))`n"
}

try {
    [System.IO.Directory]::CreateDirectory($packagesOutput) | Out-Null

    $registryToml = Get-Content -LiteralPath $registryDefinitionPath -Raw
    $registryContext = "Registry definition '$registryDefinitionPath'"
    if (
        (Get-TomlInteger `
            -Toml $registryToml `
            -Property 'schema_version' `
            -Context $registryContext) -ne 0
    ) {
        throw "$registryContext schema_version must be 0."
    }

    $registryId = Get-TomlString `
        -Toml $registryToml `
        -Property 'registry_id' `
        -Context $registryContext
    $registryDisplayName = Get-TomlString `
        -Toml $registryToml `
        -Property 'display_name' `
        -Context $registryContext
    $publisherId = Get-TomlString `
        -Toml $registryToml `
        -Property 'publisher_id' `
        -Context $registryContext `
        -Optional
    $signingKeyId = Get-TomlString `
        -Toml $registryToml `
        -Property 'signing_key_id' `
        -Context $registryContext `
        -Optional
    if (-not [string]::IsNullOrWhiteSpace($SigningKeyPath)) {
        if (
            [string]::IsNullOrWhiteSpace($publisherId) -or
            [string]::IsNullOrWhiteSpace($signingKeyId)
        ) {
            throw "$registryContext must declare publisher_id and signing_key_id when signing."
        }

        $SigningKeyPath = [System.IO.Path]::GetFullPath($SigningKeyPath)
        if (-not (Test-Path -LiteralPath $SigningKeyPath -PathType Leaf)) {
            throw "Signing key '$SigningKeyPath' does not exist."
        }
    }

    $packageManifests = @()
    if (Test-Path -LiteralPath $templatesRoot -PathType Container) {
        $rootFiles = @(Get-ChildItem -LiteralPath $templatesRoot -File -Force)
        if ($rootFiles.Count -gt 0) {
            throw "The templates root may contain namespace directories only."
        }

        $packageManifests = @(
            $namespaceDirectories = @(
                Get-ChildItem -LiteralPath $templatesRoot -Directory -Force |
                    Sort-Object Name)
            foreach ($namespaceDirectory in $namespaceDirectories) {
                $namespaceFiles = @(
                    Get-ChildItem -LiteralPath $namespaceDirectory.FullName -File -Force)
                if ($namespaceFiles.Count -gt 0) {
                    throw "Namespace '$($namespaceDirectory.Name)' may contain package directories only."
                }

                $packageDirectories = @(
                    Get-ChildItem -LiteralPath $namespaceDirectory.FullName -Directory -Force |
                        Sort-Object Name)
                foreach ($packageDirectory in $packageDirectories) {
                    $manifestPath = Join-Path -Path $packageDirectory.FullName -ChildPath (
                        'package.toml')
                    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
                        throw "Package folder '$($packageDirectory.FullName)' is missing package.toml."
                    }

                    Get-Item -LiteralPath $manifestPath
                }
            })
    }

    $moduleManifests = @()
    if (Test-Path -LiteralPath $modulesRoot -PathType Container) {
        $moduleRootFiles = @(Get-ChildItem -LiteralPath $modulesRoot -File -Force)
        if ($moduleRootFiles.Count -gt 0) {
            throw "The modules root may contain namespace directories only."
        }

        $moduleManifests = @(
            $moduleNamespaces = @(
                Get-ChildItem -LiteralPath $modulesRoot -Directory -Force |
                    Sort-Object Name)
            foreach ($moduleNamespace in $moduleNamespaces) {
                $namespaceFiles = @(
                    Get-ChildItem -LiteralPath $moduleNamespace.FullName -File -Force)
                if ($namespaceFiles.Count -gt 0) {
                    throw "Module namespace '$($moduleNamespace.Name)' may contain module directories only."
                }

                foreach ($moduleDirectory in @(
                    Get-ChildItem -LiteralPath $moduleNamespace.FullName -Directory -Force |
                        Sort-Object Name
                )) {
                    $manifestPath = Join-Path -Path $moduleDirectory.FullName -ChildPath (
                        'module.toml')
                    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
                        throw "Module folder '$($moduleDirectory.FullName)' is missing module.toml."
                    }

                    Get-Item -LiteralPath $manifestPath
                }
            })
    }

    if ($packageManifests.Count -eq 0 -and $moduleManifests.Count -eq 0) {
        throw "No template packages or modules were discovered."
    }

    $registryEntries = [System.Collections.Generic.List[object]]::new()
    $registryModules = [System.Collections.Generic.List[object]]::new()
    $catalogPackages = [System.Collections.Generic.List[object]]::new()
    $catalogModules = [System.Collections.Generic.List[object]]::new()
    $templateIds = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($packageManifest in $packageManifests) {
        $packageRoot = $packageManifest.DirectoryName
        $packageRelativePath = [System.IO.Path]::GetRelativePath(
            $templatesRoot,
            $packageManifest.FullName).Replace('\', '/')
        $pathSegments = @($packageRelativePath.Split('/'))
        if (
            $pathSegments.Count -ne 3 -or
            $pathSegments[2] -cne 'package.toml'
        ) {
            throw "Package manifest '$packageRelativePath' must use templates/<namespace>/<package>/package.toml."
        }

        Assert-NoReparsePoints `
            -Root $packageRoot `
            -Context "Package source '$packageRelativePath'"

        $packageToml = Get-Content -LiteralPath $packageManifest.FullName -Raw
        $packageContext = "Package manifest '$packageRelativePath'"
        if (
            (Get-TomlInteger `
                -Toml $packageToml `
                -Property 'schema_version' `
                -Context $packageContext) -ne 0
        ) {
            throw "$packageContext schema_version must be 0."
        }
        if (
            (Get-TomlTopLevel -Toml $packageToml) -match
                '(?m)^\s*favorite\s*='
        ) {
            throw "$packageContext cannot declare app-local property 'favorite'."
        }

        $namespace = Get-TomlString `
            -Toml $packageToml `
            -Property 'namespace' `
            -Context $packageContext
        $packageId = Get-TomlString `
            -Toml $packageToml `
            -Property 'id' `
            -Context $packageContext
        Assert-Identifier -Value $namespace -Context 'Template namespace'
        Assert-Identifier -Value $packageId -Context 'Package ID'
        if (
            $namespace -cne $pathSegments[0] -or
            $packageId -cne $pathSegments[1]
        ) {
            throw "$packageContext identity must match its namespace/package folders."
        }

        $familyId = "$namespace.$packageId"
        $name = Get-TomlString `
            -Toml $packageToml `
            -Property 'name' `
            -Context $packageContext
        $packageDescription = Get-TomlString `
            -Toml $packageToml `
            -Property 'description' `
            -Context $packageContext
        $sourceLicense = Get-TomlString `
            -Toml $packageToml `
            -Property 'source_license' `
            -Context $packageContext
        $language = Get-TomlString `
            -Toml $packageToml `
            -Property 'language' `
            -Context $packageContext
        Assert-Identifier -Value $language -Context 'Language ID'
        $licenseSummary = Get-TomlString `
            -Toml $packageToml `
            -Property 'license_summary' `
            -Context $packageContext
        $logo = Get-TomlString `
            -Toml $packageToml `
            -Property 'logo' `
            -Context $packageContext `
            -Optional
        $tags = Get-TomlStringArray `
            -Toml $packageToml `
            -Property 'tags' `
            -Context $packageContext
        $packageTables = Get-TomlTableText `
            -Toml $packageToml `
            -AllowedTables @('parameters') `
            -Context $packageContext
        $documentationRelativePath = $null
        $documentationPath = Join-Path -Path $packageRoot -ChildPath 'README.md'
        if (Test-Path -LiteralPath $documentationPath -PathType Leaf) {
            $documentationRelativePath = [System.IO.Path]::GetRelativePath(
                $sourceRootPath,
                $documentationPath).Replace('\', '/')
        }

        if (-not [string]::IsNullOrWhiteSpace($logo)) {
            Assert-SafeArchivePath `
                -RelativePath $logo `
                -Context $packageContext
            $logoPath = Join-Path -Path $packageRoot -ChildPath (
                $logo.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
            if (-not (Test-Path -LiteralPath $logoPath -PathType Leaf)) {
                throw "$packageContext logo '$logo' does not exist."
            }
        }

        $variantsRoot = Join-Path -Path $packageRoot -ChildPath 'variants'
        if (-not (Test-Path -LiteralPath $variantsRoot -PathType Container)) {
            throw "$packageContext must contain a variants directory."
        }

        $variantRootFiles = @(
            Get-ChildItem -LiteralPath $variantsRoot -File -Force)
        if ($variantRootFiles.Count -gt 0) {
            throw "$packageContext variants directory may contain variant directories only."
        }

        $variantDirectories = @(
            Get-ChildItem -LiteralPath $variantsRoot -Directory -Force |
                Sort-Object Name)
        if ($variantDirectories.Count -eq 0) {
            throw "$packageContext does not contain any variants."
        }

        $catalogVariants = [System.Collections.Generic.List[object]]::new()
        foreach ($variantDirectory in $variantDirectories) {
            $variantManifestPath = Join-Path -Path $variantDirectory.FullName -ChildPath (
                'variant.toml')
            if (-not (Test-Path -LiteralPath $variantManifestPath -PathType Leaf)) {
                throw "Variant folder '$($variantDirectory.FullName)' does not contain variant.toml."
            }

            $variantToml = Get-Content -LiteralPath $variantManifestPath -Raw
            $variantRelativePath = [System.IO.Path]::GetRelativePath(
                $templatesRoot,
                $variantManifestPath).Replace('\', '/')
            $variantContext = "Variant manifest '$variantRelativePath'"
            if (
                (Get-TomlInteger `
                    -Toml $variantToml `
                    -Property 'schema_version' `
                    -Context $variantContext) -ne 0
            ) {
                throw "$variantContext schema_version must be 0."
            }

            $variantId = Get-TomlString `
                -Toml $variantToml `
                -Property 'id' `
                -Context $variantContext
            Assert-Identifier -Value $variantId -Context 'Variant ID'
            if ($variantId -cne $variantDirectory.Name) {
                throw "$variantContext ID must match its folder name."
            }

            $description = Get-TomlString `
                -Toml $variantToml `
                -Property 'description' `
                -Context $variantContext
            $version = Get-TomlString `
                -Toml $variantToml `
                -Property 'version' `
                -Context $variantContext
            $targetOs = Get-TomlString `
                -Toml $variantToml `
                -Property 'target_os' `
                -Context $variantContext
            $buildSystem = Get-TomlString `
                -Toml $variantToml `
                -Property 'build_system' `
                -Context $variantContext
            if (
                (Get-TomlTopLevel -Toml $variantToml) -match
                    '(?m)^\s*favorite\s*='
            ) {
                throw "$variantContext cannot declare app-local property 'favorite'."
            }
            $variantTags = Get-TomlStringArray `
                -Toml $variantToml `
                -Property 'tags' `
                -Context $variantContext `
                -Optional
            $runtimeTags = [System.Collections.Generic.List[string]]::new()
            $seenTags = [System.Collections.Generic.HashSet[string]]::new(
                [System.StringComparer]::OrdinalIgnoreCase)
            foreach ($tag in @($tags) + @($variantTags)) {
                if ($seenTags.Add($tag)) {
                    $runtimeTags.Add($tag)
                }
            }
            $variantTables = Get-TomlTableText `
                -Toml $variantToml `
                -AllowedTables @('parameters', 'prerequisites') `
                -Context $variantContext
            if ($version -cnotmatch '^[A-Za-z0-9._-]+$') {
                throw "$variantContext version contains unsupported characters."
            }

            $templateId = "$familyId.$variantId"
            if (-not $templateIds.Add($templateId)) {
                throw "Discovered duplicate template ID '$templateId'."
            }

            $runtimeManifest = New-RuntimeManifest `
                -TemplateId $templateId `
                -FamilyId $familyId `
                -VariantId $variantId `
                -Name $name `
                -Description $description `
                -Version $version `
                -TargetOs $targetOs `
                -BuildSystem $buildSystem `
                -Language $language `
                -SourceLicense $sourceLicense `
                -Logo $logo `
                -Tags $runtimeTags.ToArray() `
                -PackageTables $packageTables `
                -VariantTables $variantTables
            $entries = [System.Collections.Generic.Dictionary[string, object]]::new(
                [System.StringComparer]::OrdinalIgnoreCase)
            Add-ArchiveEntry `
                -Entries $entries `
                -RelativePath 'template.toml' `
                -Content ([System.Text.UTF8Encoding]::new($false).GetBytes(
                    $runtimeManifest)) `
                -Context "Template '$templateId'"
            Add-SourceTree `
                -Entries $entries `
                -Root $packageRoot `
                -ExcludedRelativePaths @('package.toml') `
                -ExcludedPrefixes @('variants/') `
                -Context "Template '$templateId'"
            Add-SourceTree `
                -Entries $entries `
                -Root $variantDirectory.FullName `
                -ExcludedRelativePaths @('variant.toml') `
                -ExcludedPrefixes @() `
                -Context "Template '$templateId'"
            Assert-NoFileDirectoryCollisions `
                -Entries $entries `
                -Context "Template '$templateId'"
            $contentEntries = @(
                $entries.Keys |
                    Where-Object {
                        $_.StartsWith(
                            'content/',
                            [System.StringComparison]::Ordinal)
                    })
            if ($contentEntries.Count -eq 0) {
                throw "Template '$templateId' does not contain content files."
            }

            $archiveName = "$templateId-$version.zip"
            $archivePath = Join-Path -Path $packagesOutput -ChildPath $archiveName
            New-DeterministicPackageArchive `
                -Entries $entries `
                -ArchivePath $archivePath
            $archive = Get-Item -LiteralPath $archivePath
            $checksum = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).
                Hash.ToLowerInvariant()

            $registryEntries.Add([ordered] @{
                family_id = $familyId
                variant_id = $variantId
                template_id = $templateId
                name = $name
                description = $description
                version = $version
                target_os = $targetOs
                build_system = $buildSystem
                language = $language
                package_path = "packages/$archiveName"
                license_summary = $licenseSummary
                package_sha256 = $checksum
                package_size_bytes = [int64] $archive.Length
            })
            $catalogVariants.Add([ordered] @{
                id = $variantId
                template_id = $templateId
                description = $description
                version = $version
                target_os = $targetOs
                build_system = $buildSystem
                tags = @($variantTags)
                package_path = "packages/$archiveName"
                package_sha256 = $checksum
                package_size_bytes = [int64] $archive.Length
            })
        }

        $catalogPackages.Add([ordered] @{
            family_id = $familyId
            namespace = $namespace
            package_id = $packageId
            name = $name
            description = $packageDescription
            language = $language
            source_license = $sourceLicense
            license_summary = $licenseSummary
            tags = @($tags)
            documentation_path = $documentationRelativePath
            variants = @(
                $catalogVariants |
                    Sort-Object {
                        [string] $_.id
                    }, {
                        [string] $_.version
                    })
        })
    }

    $moduleIds = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($moduleManifest in $moduleManifests) {
        $moduleRoot = $moduleManifest.DirectoryName
        $moduleRelativePath = [System.IO.Path]::GetRelativePath(
            $modulesRoot,
            $moduleManifest.FullName).Replace('\', '/')
        $pathSegments = @($moduleRelativePath.Split('/'))
        if (
            $pathSegments.Count -ne 3 -or
            $pathSegments[2] -cne 'module.toml'
        ) {
            throw "Module manifest '$moduleRelativePath' must use modules/<namespace>/<module>/module.toml."
        }

        Assert-Identifier -Value $pathSegments[0] -Context 'Module namespace'
        Assert-Identifier -Value $pathSegments[1] -Context 'Module folder'
        Assert-NoReparsePoints `
            -Root $moduleRoot `
            -Context "Module source '$moduleRelativePath'"

        $moduleToml = Get-Content -LiteralPath $moduleManifest.FullName -Raw
        $moduleContext = "Module manifest '$moduleRelativePath'"
        if (
            (Get-TomlInteger `
                -Toml $moduleToml `
                -Property 'schema_version' `
                -Context $moduleContext) -ne 0
        ) {
            throw "$moduleContext schema_version must be 0."
        }

        if (
            (Get-TomlTopLevel -Toml $moduleToml) -match
                '(?m)^\s*favorite\s*='
        ) {
            throw "$moduleContext cannot declare app-local property 'favorite'."
        }

        $moduleId = Get-TomlString `
            -Toml $moduleToml `
            -Property 'id' `
            -Context $moduleContext
        $expectedModuleId = "$($pathSegments[0]).$($pathSegments[1])"
        if ($moduleId -cne $expectedModuleId) {
            throw "$moduleContext ID must be '$expectedModuleId' to match its folders."
        }

        $moduleName = Get-TomlString `
            -Toml $moduleToml `
            -Property 'name' `
            -Context $moduleContext
        $moduleDescription = Get-TomlString `
            -Toml $moduleToml `
            -Property 'description' `
            -Context $moduleContext
        $moduleVersion = Get-TomlString `
            -Toml $moduleToml `
            -Property 'version' `
            -Context $moduleContext
        $moduleLanguage = Get-TomlString `
            -Toml $moduleToml `
            -Property 'language' `
            -Context $moduleContext
        Assert-Identifier -Value $moduleLanguage -Context 'Module language'
        $moduleLicense = Get-TomlString `
            -Toml $moduleToml `
            -Property 'source_license' `
            -Context $moduleContext
        $moduleTags = Get-TomlStringArray `
            -Toml $moduleToml `
            -Property 'tags' `
            -Context $moduleContext `
            -Optional
        $versionedModuleId = "$moduleId@$moduleVersion"
        if (-not $moduleIds.Add($versionedModuleId)) {
            throw "Discovered duplicate module '$versionedModuleId'."
        }

        $entries = [System.Collections.Generic.Dictionary[string, object]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        Add-SourceTree `
            -Entries $entries `
            -Root $moduleRoot `
            -ExcludedRelativePaths @() `
            -ExcludedPrefixes @() `
            -Context "Module '$moduleId'"
        Assert-NoFileDirectoryCollisions `
            -Entries $entries `
            -Context "Module '$moduleId'"
        $contentEntries = @(
            $entries.Keys |
                Where-Object {
                    $_.StartsWith(
                        'content/',
                        [System.StringComparison]::Ordinal)
                })
        if ($contentEntries.Count -eq 0) {
            throw "Module '$moduleId' does not contain content files."
        }

        $archiveName = "$moduleId-$moduleVersion.zip"
        $archivePath = Join-Path -Path $packagesOutput -ChildPath $archiveName
        New-DeterministicPackageArchive `
            -Entries $entries `
            -ArchivePath $archivePath
        $archive = Get-Item -LiteralPath $archivePath
        $checksum = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).
            Hash.ToLowerInvariant()

        $registryModules.Add([ordered] @{
            module_id = $moduleId
            name = $moduleName
            description = $moduleDescription
            version = $moduleVersion
            language = $moduleLanguage
            package_path = "packages/$archiveName"
            license_summary = $moduleLicense
            package_sha256 = $checksum
            package_size_bytes = [int64] $archive.Length
            tags = @($moduleTags)
        })
        $catalogModules.Add([ordered] @{
            id = $moduleId
            name = $moduleName
            description = $moduleDescription
            version = $moduleVersion
            language = $moduleLanguage
            source_license = $moduleLicense
            tags = @($moduleTags)
            package_path = "packages/$archiveName"
            package_sha256 = $checksum
            package_size_bytes = [int64] $archive.Length
        })
    }

    $registry = [ordered] @{
        schema_version = 1
        registry_id = $registryId
        display_name = $registryDisplayName
        templates = @(
            $registryEntries |
                Sort-Object {
                    [string] $_.template_id
                }, {
                    [string] $_.version
                })
        modules = @(
            $registryModules |
                Sort-Object {
                    [string] $_.module_id
                }, {
                    [string] $_.version
                })
    }
    $registryJson = Normalize-LineEndings -Text (
        $registry | ConvertTo-Json -Depth 8)
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText(
        (Join-Path -Path $stagingRoot -ChildPath 'registry.json'),
        "$registryJson`n",
        $utf8)

    $catalog = [ordered] @{
        schema_version = 0
        registry_id = $registryId
        display_name = $registryDisplayName
        publisher_id = $publisherId
        signing_key_id = $signingKeyId
        packages = @(
            $catalogPackages |
                Sort-Object {
                    [string] $_.family_id
                })
        modules = @(
            $catalogModules |
                Sort-Object {
                    [string] $_.id
                }, {
                    [string] $_.version
                })
    }
    $catalogJson = Normalize-LineEndings -Text (
        $catalog | ConvertTo-Json -Depth 10)
    [System.IO.File]::WriteAllText(
        (Join-Path -Path $stagingRoot -ChildPath 'catalog.json'),
        "$catalogJson`n",
        $utf8)
    $catalogMarkdown = New-CatalogMarkdown `
        -RegistryDisplayName $registryDisplayName `
        -Packages @($catalog.packages) `
        -Modules @($catalog.modules)
    [System.IO.File]::WriteAllText(
        (Join-Path -Path $stagingRoot -ChildPath 'catalog.md'),
        $catalogMarkdown,
        $utf8)

    if (-not [string]::IsNullOrWhiteSpace($SigningKeyPath)) {
        $indexBytes = [System.IO.File]::ReadAllBytes(
            (Join-Path -Path $stagingRoot -ChildPath 'registry.json'))
        $indexHash = [System.Security.Cryptography.SHA256]::HashData($indexBytes)
        $rsa = [System.Security.Cryptography.RSA]::Create()
        try {
            $rsa.ImportFromPem(
                [System.IO.File]::ReadAllText($SigningKeyPath))
            if ($rsa.KeySize -lt 2048) {
                throw 'Registry signing keys must be at least 2048-bit RSA keys.'
            }

            $signatureBytes = $rsa.SignHash(
                $indexHash,
                [System.Security.Cryptography.HashAlgorithmName]::SHA256,
                [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
            $signatureDocument = [ordered] @{
                schema_version = 0
                publisher_id = $publisherId
                key_id = $signingKeyId
                algorithm = 'rsa-pkcs1-sha256'
                index_sha256 = [System.Convert]::ToHexString(
                    $indexHash).ToLowerInvariant()
                signature = [System.Convert]::ToBase64String($signatureBytes)
            }
            $signatureJson = Normalize-LineEndings -Text (
                $signatureDocument | ConvertTo-Json -Depth 4)
            [System.IO.File]::WriteAllText(
                (Join-Path -Path $stagingRoot -ChildPath (
                    'registry.json.sig.json')),
                "$signatureJson`n",
                $utf8)
        }
        finally {
            $rsa.Dispose()
        }
    }

    Move-Item -LiteralPath $stagingRoot -Destination $outputRootPath
    Write-Host (
        "Discovered $($registryEntries.Count) template variants and " +
        "$($registryModules.Count) modules; wrote " +
        "registry artifacts to '$outputRootPath'.")
}
finally {
    if (Test-Path -LiteralPath $stagingRoot -PathType Container) {
        $resolvedStaging = [System.IO.Path]::GetFullPath($stagingRoot)
        $relativeToParent = [System.IO.Path]::GetRelativePath(
            $outputParent.FullName,
            $resolvedStaging)
        if (
            [System.IO.Path]::IsPathRooted($relativeToParent) -or
            $relativeToParent -eq '..' -or
            $relativeToParent.StartsWith(
                "..$([System.IO.Path]::DirectorySeparatorChar)",
                [System.StringComparison]::Ordinal)
        ) {
            throw "Refusing to clean staging path '$resolvedStaging'."
        }

        Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
    }
}
