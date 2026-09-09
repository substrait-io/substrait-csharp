param(
    [Parameter(Mandatory = $true)]
    [string] $PackagePath,

    [Parameter(Mandatory = $true)]
    [string] $SymbolsPackagePath,

    [Parameter(Mandatory = $true)]
    [string] $ExpectedVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Equal {
    param(
        [object] $Expected,
        [object] $Actual,
        [string] $Description
    )

    if ($Expected -ne $Actual) {
        throw "$Description. Expected '$Expected', found '$Actual'."
    }
}

function Assert-ArchiveEntries {
    param(
        [string] $Path,
        [string[]] $RequiredEntries
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $Path))
    try {
        $entries = [System.Collections.Generic.HashSet[string]]::new(
            [string[]] $archive.Entries.FullName,
            [System.StringComparer]::Ordinal)

        foreach ($requiredEntry in $RequiredEntries) {
            if (-not $entries.Contains($requiredEntry)) {
                throw "Package '$Path' is missing '$requiredEntry'."
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Read-Nuspec {
    param([string] $Path)

    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $Path))
    try {
        $nuspecEntries = @($archive.Entries | Where-Object FullName -Like '*.nuspec')
        Assert-Equal 1 $nuspecEntries.Count "Package '$Path' nuspec count"

        $reader = [System.IO.StreamReader]::new($nuspecEntries[0].Open())
        try {
            return [xml] $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Read-SourceLinkUrls {
    param(
        [string] $Path,
        [string] $TargetFramework
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $Path))
    try {
        $pdbEntry = $archive.GetEntry("lib/$TargetFramework/Microsoft.Substrait.pdb")
        if ($null -eq $pdbEntry) {
            throw "Package '$Path' is missing the Source Link PDB for '$TargetFramework'."
        }

        $pdbStream = [System.IO.MemoryStream]::new()
        $entryStream = $pdbEntry.Open()
        try {
            $entryStream.CopyTo($pdbStream)
        }
        finally {
            $entryStream.Dispose()
        }

        $pdbStream.Position = 0
        $provider = [System.Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
        try {
            $reader = $provider.GetMetadataReader()
            $module = [System.Reflection.Metadata.Ecma335.MetadataTokens]::EntityHandle(
                [System.Reflection.Metadata.Ecma335.TableIndex]::Module,
                1)
            $sourceLinkKind = [Guid] 'CC110556-A091-4D38-9FEC-25AB9A351A6A'
            $sourceLinkRecords = @(
                $reader.GetCustomDebugInformation($module) |
                    ForEach-Object { $reader.GetCustomDebugInformation($_) } |
                    Where-Object { $reader.GetGuid($_.Kind) -eq $sourceLinkKind })
            Assert-Equal 1 $sourceLinkRecords.Count 'Source Link record count'

            $sourceLinkBytes = $reader.GetBlobBytes($sourceLinkRecords[0].Value)
            $sourceLink = [System.Text.Encoding]::UTF8.GetString($sourceLinkBytes) | ConvertFrom-Json
            return @($sourceLink.documents.PSObject.Properties.Value)
        }
        finally {
            $provider.Dispose()
            $pdbStream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

Assert-ArchiveEntries $PackagePath @(
    'Microsoft.Substrait.nuspec',
    'lib/net10.0/Microsoft.Substrait.dll',
    'lib/net10.0/Microsoft.Substrait.xml',
    'lib/net8.0/Microsoft.Substrait.dll',
    'lib/net8.0/Microsoft.Substrait.xml',
    'lib/netstandard2.0/Microsoft.Substrait.dll',
    'lib/netstandard2.0/Microsoft.Substrait.xml',
    'README.md'
)
Assert-ArchiveEntries $SymbolsPackagePath @(
    'Microsoft.Substrait.nuspec',
    'lib/net10.0/Microsoft.Substrait.pdb',
    'lib/net8.0/Microsoft.Substrait.pdb',
    'lib/netstandard2.0/Microsoft.Substrait.pdb'
)

$nuspec = Read-Nuspec $PackagePath
$namespaceManager = [System.Xml.XmlNamespaceManager]::new($nuspec.NameTable)
$namespaceManager.AddNamespace('n', $nuspec.DocumentElement.NamespaceURI)
$metadata = $nuspec.SelectSingleNode('/n:package/n:metadata', $namespaceManager)

Assert-Equal 'Microsoft.Substrait' $metadata.id 'Package ID'
Assert-Equal $ExpectedVersion $metadata.version 'Package version'
Assert-Equal 'Microsoft' $metadata.authors 'Package authors'
Assert-Equal 'Apache-2.0' $metadata.license.InnerText 'Package license'
Assert-Equal 'expression' $metadata.license.type 'Package license type'
Assert-Equal 'README.md' $metadata.readme 'Package readme'
Assert-Equal 'git' $metadata.repository.type 'Repository type'
Assert-Equal 'https://github.com/substrait-io/substrait-csharp' $metadata.repository.url 'Repository URL'

if ([string]::IsNullOrWhiteSpace($metadata.repository.commit)) {
    throw 'Package repository commit is missing.'
}

$expectedRepositorySource = "https://raw.githubusercontent.com/substrait-io/substrait-csharp/$($metadata.repository.commit)/*"
$expectedSubstraitSource = 'https://raw.githubusercontent.com/substrait-io/substrait/d430e521f203aec6a4e06731d4bfd68cdf61f443/*'
foreach ($targetFramework in @('net10.0', 'net8.0', 'netstandard2.0')) {
    $sourceLinkUrls = @(Read-SourceLinkUrls $SymbolsPackagePath $targetFramework)
    if ($sourceLinkUrls -notcontains $expectedRepositorySource) {
        throw "Source Link mapping '$expectedRepositorySource' is missing for '$targetFramework'."
    }

    if ($sourceLinkUrls -notcontains $expectedSubstraitSource) {
        throw "Source Link mapping '$expectedSubstraitSource' is missing for '$targetFramework'."
    }
}

$expectedDependencyGroups = [ordered]@{
    'net10.0' = [ordered]@{
        'Antlr4.Runtime.Standard' = '4.13.1'
        'Google.Protobuf' = '3.26.1'
        'YamlDotNet' = '15.1.2'
    }
    'net8.0' = [ordered]@{
        'Antlr4.Runtime.Standard' = '4.13.1'
        'Google.Protobuf' = '3.26.1'
        'YamlDotNet' = '15.1.2'
    }
    '.NETStandard2.0' = [ordered]@{
        'Antlr4.Runtime.Standard' = '4.13.1'
        'Google.Protobuf' = '3.26.1'
        'IndexRange' = '1.0.3'
        'Microsoft.Bcl.HashCode' = '6.0.0'
        'System.Collections.Immutable' = '8.0.0'
        'System.Memory' = '4.5.5'
        'YamlDotNet' = '15.1.2'
    }
}
$dependencyGroups = @($metadata.dependencies.group)
Assert-Equal $expectedDependencyGroups.Count $dependencyGroups.Count 'Dependency group count'

foreach ($targetFramework in $expectedDependencyGroups.Keys) {
    $matchingGroups = @($dependencyGroups | Where-Object targetFramework -EQ $targetFramework)
    Assert-Equal 1 $matchingGroups.Count "Dependency group '$targetFramework' count"

    $expectedDependencies = $expectedDependencyGroups[$targetFramework]
    $actualDependencies = @($matchingGroups[0].dependency)
    Assert-Equal $expectedDependencies.Count $actualDependencies.Count "Runtime dependency count for '$targetFramework'"

    foreach ($dependency in $actualDependencies) {
        if (-not $expectedDependencies.Contains($dependency.id)) {
            throw "Unexpected runtime dependency '$($dependency.id)' for '$targetFramework'."
        }

        Assert-Equal $expectedDependencies[$dependency.id] $dependency.version "Dependency '$($dependency.id)' version for '$targetFramework'"
    }
}

Write-Host "Validated Microsoft.Substrait $ExpectedVersion package and symbols."