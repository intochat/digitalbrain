param(
    [Parameter(Mandatory = $true)]
    [string]$FeedDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$requiredElements = @('id', 'version', 'description', 'authors', 'projectUrl', 'license', 'icon', 'readme', 'tags')
$packages = Get-ChildItem $FeedDirectory -Filter '*.nupkg'
if (-not $packages) {
    throw "No packages found in $FeedDirectory."
}

foreach ($package in $packages) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $nuspecEntry = $archive.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
        if (-not $nuspecEntry) {
            throw "$($package.Name) contains no nuspec."
        }
        $reader = New-Object System.IO.StreamReader ($nuspecEntry.Open())
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $metadata = $nuspec.package.metadata
        foreach ($element in $requiredElements) {
            if ([string]::IsNullOrWhiteSpace([string]$metadata.$element)) {
                throw "$($package.Name) is missing nuspec element '$element'."
            }
        }
        if ([string]::IsNullOrWhiteSpace($metadata.repository.url) -or [string]::IsNullOrWhiteSpace($metadata.repository.commit)) {
            throw "$($package.Name) is missing repository url or commit metadata."
        }
        foreach ($contentFile in @($metadata.icon, $metadata.readme)) {
            if (-not ($archive.Entries.FullName -contains $contentFile)) {
                throw "$($package.Name) does not contain declared file '$contentFile'."
            }
        }

        Write-Output ("{0} {1} ok: {2}" -f $metadata.id, $metadata.version, $metadata.description)
    }
    finally {
        $archive.Dispose()
    }
}
