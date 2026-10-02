param([string]$SourceDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$provenance = Get-Content (Join-Path $root 'NyaIme/Assets/MOZC-DICTIONARY-PROVENANCE.json') -Raw | ConvertFrom-Json
if (!$SourceDirectory) { $SourceDirectory = Join-Path $root '.dictionary-source' }
New-Item -ItemType Directory -Path $SourceDirectory -Force | Out-Null
foreach ($file in $provenance.files | Where-Object { $_.name -match '^dictionary[0-9]{2}\.txt$|^connection_single_column\.txt$' }) {
    $path = Join-Path $SourceDirectory $file.name
    if (!(Test-Path -LiteralPath $path)) {
        $url = "https://raw.githubusercontent.com/google/mozc/$($provenance.observedCommit)/src/data/dictionary_oss/$($file.name)"
        Invoke-WebRequest -Uri $url -OutFile $path
    }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) {
        throw "Mozc source hash mismatch: $($file.name)"
    }
}
dotnet run --project (Join-Path $root 'tools/DictionaryBuilder') -- $SourceDirectory (Join-Path $root 'NyaIme/Assets/offline-dictionary.gz')
if ($LASTEXITCODE -ne 0) { throw 'Dictionary generation failed' }
