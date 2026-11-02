param([Parameter(Mandatory=$true)][string]$Tools,[Parameter(Mandatory=$true)][string]$FixtureBuild,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Output){throw 'New fixture directory required'}
New-Item -ItemType Directory -Path $Output | Out-Null
$utf8=New-Object Text.UTF8Encoding($true)
$cases=@(
    @{Name='saved-ui-failure';Retained=$false;Run='saved';Actions='product1-run,product1-review';Code=0},
    @{Name='no-run-ui-failure';Retained=$false;Run='no-run';Actions='product1-run';Code=1},
    @{Name='retained-review-only';Retained=$true;Run='saved';Actions='product1-review';Code=0}
)
$previous=$env:PIXORA_PRODUCT1_FIXTURE_RUN
try {
foreach($case in $cases){
    $folder=Join-Path $Output ($case.Name+' space '+[char]0x3A9);New-Item -ItemType Directory -Path $folder | Out-Null
    foreach($name in @('Product1Session_Start.ps1','SessionProduct1_Experiment.ps1')){
        [IO.File]::WriteAllText((Join-Path $folder $name),(Get-Content -LiteralPath (Join-Path $Tools $name) -Raw -Encoding UTF8),$utf8)
    }
    foreach($f in Get-ChildItem -LiteralPath $FixtureBuild -File){Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $folder $f.Name)}
    Copy-Item -LiteralPath (Join-Path $FixtureBuild 'FrontierLauncherFixture.exe') -Destination (Join-Path $folder 'Pixora.exe')
    [IO.File]::WriteAllText((Join-Path $folder 'OFFLINE_FIXTURE_ONLY'),'Filesystem only; no native input or capture')
    $product=Join-Path $folder 'Product1_Session';New-Item -ItemType Directory -Path $product | Out-Null
    [IO.File]::WriteAllText((Join-Path $product 'package.json'),'{}')
    $binding=Join-Path $folder 'Analysis_Session1_fixture';New-Item -ItemType Directory -Path $binding | Out-Null
    [IO.File]::WriteAllText((Join-Path $binding 'session-binding.json'),'{"status":"SESSION_TRANSFER_CONFIRMED","bindingActive":true,"liveBindingId":"fixture"}',$utf8)
    if($case.Retained){
        $run=Join-Path $product 'Data\last-run\fixture-run';New-Item -ItemType Directory -Path $run | Out-Null
        [IO.File]::WriteAllText((Join-Path $run 'experimental-texel-plan.json'),'{}')
    }
    $inputFile=Join-Path $folder 'input.txt';[IO.File]::WriteAllText($inputFile,"`n")
    $env:PIXORA_PRODUCT1_FIXTURE_RUN=$case.Run
    $words='-NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $folder 'Product1Session_Start.ps1')+'" -Mode Run -Package "'+$folder+'"'
    $job=Start-Process powershell.exe -ArgumentList $words -WorkingDirectory $folder -WindowStyle Hidden -Wait -PassThru -RedirectStandardInput $inputFile -RedirectStandardOutput (Join-Path $folder 'stdout.log') -RedirectStandardError (Join-Path $folder 'stderr.log')
    $events=@(Get-Content -LiteralPath (Join-Path $folder 'fixture-events.jsonl') | ForEach-Object {ConvertFrom-Json $_})
    $actions=($events | ForEach-Object {$_.action}) -join ','
    if($job.ExitCode -ne $case.Code -or $actions -cne $case.Actions -or @($events|Where-Object {$_.nativeInput -ne $false}).Count){throw "Launcher mismatch $($case.Name): exit$($job.ExitCode), $actions"}
    if(-not $case.Retained){
        $log=Get-Content -LiteralPath (Join-Path $folder 'stdout.log') -Raw
        if(-not $log.Contains('Pixora exit code: 9') -or -not $log.Contains('synthetic UI shutdown failure')){throw 'UI exit code/reason was lost'}
    }
    Write-Output ('PASS '+$case.Name)
}
} finally {$env:PIXORA_PRODUCT1_FIXTURE_RUN=$previous}
Write-Output 'ALL3 PRODUCT1 LAUNCHER CASES PASSED; nativeInput=false'
