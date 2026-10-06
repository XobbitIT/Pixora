param([string]$PackageDirectory,[string]$SourceDirectory,[switch]$WriteDocuments,[switch]$SelfTest)
$ErrorActionPreference='Stop'

function Assert-PixoraVersionValues([string]$ExpectedVersion,[hashtable]$Values) {
    foreach($entry in $Values.GetEnumerator()) {
        if($entry.Value -cne $ExpectedVersion) { throw "Package version mismatch in $($entry.Key): $($entry.Value), expected $ExpectedVersion" }
    }
}
function Get-PixoraHeadingVersion([string]$Path,[string]$ExpectedVersion) {
    $heading=(Get-Content -LiteralPath $Path -Encoding UTF8 -TotalCount 1)
    $full=[regex]::Match($heading,'\b\d+\.\d+\.\d+-beta\.\d+\b')
    if($full.Success){return $full.Value}
    $beta=[regex]::Match($heading,'\bbeta\.\d+\b')
    if($beta.Success){return ($ExpectedVersion.Split('-')[0]+'-'+$beta.Value)}
    throw "Missing version heading: $Path"
}
function Test-PixoraPackageVersions([string]$Directory,[string]$ExpectedVersion) {
    $manifest=Get-Content -LiteralPath (Join-Path $Directory 'Build_Manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $values=@{manifest=[string]$manifest.version}
    foreach($path in @('README_UA.txt','CHANGES_UA.md','Testing/Test_Checklist_UA.md')) {
        $values[$path]=Get-PixoraHeadingVersion (Join-Path $Directory $path) $ExpectedVersion
    }
    foreach($name in @('Pixora.dll','CanvasForge.Core.dll')) {
        $info=[System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $Directory $name))
        $values[$name]=([string]$info.ProductVersion).Split('+')[0]
    }
    Assert-PixoraVersionValues $ExpectedVersion $values
    Write-Output "PASS package versions: $ExpectedVersion (manifest, README, changes, checklist, both DLLs)"
}
if($SelfTest) {
    $version='1.0.14-beta.29';$keys=@('manifest','README','changes','checklist','Pixora.dll','CanvasForge.Core.dll')
    $values=@{};foreach($key in $keys){$values[$key]=$version};Assert-PixoraVersionValues $version $values
    foreach($key in $keys) {
        $bad=$values.Clone();$bad[$key]='1.0.14-beta.28';$rejected=$false
        try{Assert-PixoraVersionValues $version $bad}catch{$rejected=$true}
        if(!$rejected){throw "Regression: accepted mismatched $key"}
    }
    Write-Output 'PASS packaging regression: matching versions accepted; all six mismatches rejected'
}
if($PackageDirectory) {
    if(!$SourceDirectory){$SourceDirectory=Split-Path $PSScriptRoot -Parent}
    $version=[string]([xml](Get-Content -LiteralPath (Join-Path $SourceDirectory 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
    if($WriteDocuments) {
        $number=[regex]::Match($version,'-beta\.(\d+)$').Groups[1].Value
        if(!$number){throw "Unsupported release version: $version"}
        New-Item -ItemType Directory -Path (Join-Path $PackageDirectory 'Testing') -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $SourceDirectory "CHANGES_BETA${number}_UA.md") -Destination (Join-Path $PackageDirectory 'CHANGES_UA.md') -Force
        Copy-Item -LiteralPath (Join-Path $SourceDirectory "TESTING_BETA${number}_UA.md") -Destination (Join-Path $PackageDirectory 'Testing/Test_Checklist_UA.md') -Force
        $readme="Pixora $version`n`nРозпакуй увесь архів в окрему папку та запусти Pixora.exe.`nЯкщо Windows просить runtime, встанови .NET 8 Desktop Runtime x64.`nПорядок тестування: Testing/Test_Checklist_UA.md. Зміни: CHANGES_UA.md.`nНовий живий тест у Rust потрібен; офлайн перевірки не підтверджують покриття в грі.`n"
        Set-Content -LiteralPath (Join-Path $PackageDirectory 'README_UA.txt') -Value $readme -Encoding UTF8
        $manifestPath=Join-Path $PackageDirectory 'Build_Manifest.json'
        if(!(Test-Path -LiteralPath $manifestPath)) {
            @{version=$version;inGameTest=$false;commit=$env:GITHUB_SHA} | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8
        }
    }
    Test-PixoraPackageVersions $PackageDirectory $version
}
