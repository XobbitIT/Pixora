param([string]$SourceDirectory,[switch]$Check)
$ErrorActionPreference='Stop'
if(!$SourceDirectory){$SourceDirectory=Split-Path $PSScriptRoot -Parent}
$version=[string]([xml](Get-Content -LiteralPath (Join-Path $SourceDirectory 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
foreach($name in @('README.md','README_UA.md','CURRENT_SNAPSHOT.txt')) {
    $path=Join-Path $SourceDirectory $name
    $text=Get-Content -LiteralPath $path -Raw -Encoding UTF8
    $lines=$text -split '\r?\n',2
    $found=[regex]::Match($lines[0],'\b\d+\.\d+\.\d+-beta\.\d+\b')
    if(!$found.Success){throw "Missing current version heading: $name"}
    if($Check) {
        if($found.Value -cne $version){throw "Current source version mismatch: $name ($($found.Value), expected $version)"}
    } else {
        $first=$lines[0].Replace($found.Value,$version)
        [System.IO.File]::WriteAllText($path,$first+"`n"+$lines[1],(New-Object System.Text.UTF8Encoding($false)))
    }
}
Write-Output "PASS current source documents: $version"
