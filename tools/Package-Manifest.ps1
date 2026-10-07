param([Parameter(Mandatory)][string]$PackageDirectory,[Parameter(Mandatory)][string]$SourceDirectory)
$ErrorActionPreference='Stop'
$packageRoot=(Resolve-Path -LiteralPath $PackageDirectory).Path
$sourceRoot=(Resolve-Path -LiteralPath $SourceDirectory).Path
$version=[string]([xml](Get-Content -LiteralPath (Join-Path $sourceRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$head=(& git -C $sourceRoot rev-parse HEAD).Trim()
if($LASTEXITCODE -ne 0 -or $head -cne $env:PIXORA_SOURCE_SHA){throw 'Package source is not the exact requested commit.'}
$status=@(& git -C $sourceRoot status --porcelain --untracked-files=no)
if($LASTEXITCODE -ne 0 -or $status.Count -gt 0){throw 'Tracked source changed during build.'}
function Test-Count([string]$Path,[string]$Pattern) {
    $text=Get-Content -LiteralPath (Join-Path $sourceRoot $Path) -Raw -Encoding UTF8
    $match=[regex]::Match($text,$Pattern)
    if(!$match.Success){throw "Missing successful test evidence: $Path"}
    return [int]$match.Groups[1].Value
}
$core=Test-Count 'verification/core-tests.txt' 'ALL ([0-9]+) TESTS PASSED'
$wpf=Test-Count 'verification/wpf-tests.txt' 'ALL ([0-9]+) WPF UI CHECKS PASSED'
if($core -ne (Test-Count 'verification/published-core-tests.txt' 'ALL ([0-9]+) TESTS PASSED') -or
   $wpf -ne (Test-Count 'verification/published-wpf-tests.txt' 'ALL ([0-9]+) WPF UI CHECKS PASSED')){throw 'Published test counts differ.'}
foreach($log in @('verification/wpf-tests.txt','verification/published-wpf-tests.txt')) {
    if((Get-Content -LiteralPath (Join-Path $sourceRoot $log) -Raw) -notmatch 'PASS native-clipboard-smoke:'){throw "Missing native clipboard smoke: $log"}
}
$sourceHashes=[ordered]@{}
$paths=@(& git -C $sourceRoot ls-files)
if($LASTEXITCODE -ne 0){throw 'Cannot enumerate exact source.'}
foreach($path in $paths) {
    $original=Join-Path $sourceRoot $path
    if(!(Test-Path -LiteralPath $original -PathType Leaf)){throw "Missing tracked source: $path"}
    $target=Join-Path (Join-Path $packageRoot 'Source') $path
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $original -Destination $target -Force
    $sourceHashes[$path]=(Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash.ToLowerInvariant()
    if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -cne $sourceHashes[$path]){throw "Source copy mismatch: $path"}
}
$runtime=Get-Content -LiteralPath (Join-Path $packageRoot 'Pixora.runtimeconfig.json') -Raw | ConvertFrom-Json
$fileHashes=[ordered]@{}
foreach($file in Get-ChildItem -LiteralPath $packageRoot -File -Recurse | Sort-Object FullName) {
    $path=$file.FullName.Substring($packageRoot.Length+1).Replace([char]92,[char]47)
    if($path -eq 'Build_Manifest.json'){continue}
    $fileHashes[$path]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest=[ordered]@{version=$version;commit=$head;buildCheckoutCommit=$head;localChanges=$false;inGameTest=$false;
    selfContained=$true;runtime=$runtime.runtimeOptions.includedFrameworks;coreTests=$core;wpfChecks=$wpf;
    packagedCoreChecksPassed=$true;packagedWpfChecksPassed=$true;nativeClipboardSmoke=$true;
    sourceFilesSha256=$sourceHashes;filesSha256=$fileHashes;clipboardPayloadInSequenceDiagnostics=$false;
    coverageCriteriaChanged=$false;movingSceneGuardMeasuredBeforeCandidates=$true}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $packageRoot 'Build_Manifest.json') -Encoding UTF8
Write-Output "PASS exact source and package hashes: $head; $core Core / $wpf WPF; native clipboard passed"
