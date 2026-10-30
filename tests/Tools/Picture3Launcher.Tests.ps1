param([Parameter(Mandatory=$true)][string]$Tools,[Parameter(Mandatory=$true)][string]$FixtureBuild,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Output){throw 'New fixture directory required'}
New-Item -ItemType Directory -Path $Output | Out-Null
$cases=@(
    @{Name='complete';Input="`n";Fail='';Mode='Run';Actions='pic3-preflight,pic3-run,pic3-review';Code=0},
    @{Name='cancel';Input="Q`n";Fail='';Mode='Run';Actions='pic3-preflight';Code=2},
    @{Name='eof';Input='';Fail='';Mode='Run';Actions='pic3-preflight';Code=2},
    @{Name='preflight-failed';Input="`n";Fail='pic3-preflight';Mode='Run';Actions='pic3-preflight';Code=1},
    @{Name='run-failed';Input="`n";Fail='pic3-run';Mode='Run';Actions='pic3-preflight,pic3-run,pic3-review';Code=1},
    @{Name='review-failed';Input="`n";Fail='pic3-review';Mode='Run';Actions='pic3-preflight,pic3-run,pic3-review';Code=1},
    @{Name='check';Input='';Fail='';Mode='Check';Actions='pic3-preflight';Code=0},
    @{Name='retained-complete';Input='';Fail='';Mode='Run';Actions='pic3-review';Code=0;Retained='complete'},
    @{Name='retained-failed';Input='';Fail='';Mode='Run';Actions='pic3-review';Code=1;Retained='failed'},
    @{Name='retained-unknown';Input='';Fail='';Mode='Run';Actions='pic3-review';Code=1;Retained='unknown'},
    @{Name='retained-startup-failed';Input='';Fail='';Mode='Run';Actions='pic3-review';Code=1;Retained='flat'}
)
$results=@();$utf8=New-Object Text.UTF8Encoding($true)
foreach($case in $cases){
    $folder=Join-Path $Output ($case.Name+' space '+[char]0x3A9);New-Item -ItemType Directory -Path $folder | Out-Null
    [IO.File]::WriteAllText((Join-Path $folder 'Picture3_Experiment.ps1'),(Get-Content -LiteralPath (Join-Path $Tools 'Picture3_Experiment.ps1') -Raw -Encoding UTF8),$utf8)
    foreach($f in Get-ChildItem -LiteralPath $FixtureBuild -File){Copy-Item -LiteralPath $f.FullName -Destination (Join-Path $folder $f.Name)}
    Copy-Item -LiteralPath (Join-Path $FixtureBuild 'FrontierLauncherFixture.exe') -Destination (Join-Path $folder 'Pixora.exe')
    [IO.File]::WriteAllText((Join-Path $folder 'OFFLINE_FIXTURE_ONLY'),'No product references, native input, or capture')
    $exp=Join-Path $folder 'Picture3_Experiment';New-Item -ItemType Directory -Path $exp | Out-Null;[IO.File]::WriteAllText((Join-Path $exp 'protocol.json'),'{}')
    if($case.Retained){
        [IO.File]::WriteAllText((Join-Path $exp 'attempted.json'),'{}')
        if($case.Retained -ne 'unknown'){
            $status=if($case.Retained -eq 'complete'){'DATA_COLLECTED'}else{'INCOMPLETE'}
            $record=if($case.Retained -eq 'flat'){@{status='INCOMPLETE';reason='original_startup_reason'}}else{@{result=@{Status=$status;Reason='original_startup_reason'}}}
            [IO.File]::WriteAllText((Join-Path $exp 'series-status.json'),($record|ConvertTo-Json -Depth 5),$utf8)
        }
    }
    $inputFile=Join-Path $folder 'input.txt';[IO.File]::WriteAllText($inputFile,$case.Input,(New-Object Text.UTF8Encoding($false)))
    $env:PIXORA_PIC3_FIXTURE_FAIL=$case.Fail
    $words='-NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $folder 'Picture3_Experiment.ps1')+'" -Mode '+$case.Mode+' -Package "'+$folder+'"'
    $job=Start-Process -FilePath 'powershell.exe' -ArgumentList $words -WorkingDirectory $folder -WindowStyle Hidden -Wait -PassThru -RedirectStandardInput $inputFile -RedirectStandardOutput (Join-Path $folder 'stdout.log') -RedirectStandardError (Join-Path $folder 'stderr.log')
    $events=Get-Content -LiteralPath (Join-Path $folder 'fixture-events.jsonl') | ForEach-Object {ConvertFrom-Json $_}
    $actions=($events | ForEach-Object {$_.action}) -join ','
    if($job.ExitCode -ne $case.Code -or $actions -cne $case.Actions -or @($events|Where-Object {$_.nativeInput -ne $false}).Count -ne 0){throw "Launcher mismatch $($case.Name): exit$($job.ExitCode), $actions"}
    if($case.Retained -and $case.Retained -ne 'unknown' -and -not(Select-String -LiteralPath (Join-Path $folder 'stdout.log') -SimpleMatch 'original_startup_reason' -Quiet)){throw 'Retained attempt hid its original failure reason'}
    $results+=@{name=$case.Name;exitCode=$job.ExitCode;actions=$actions;passed=$true}
}
Remove-Item Env:PIXORA_PIC3_FIXTURE_FAIL -ErrorAction SilentlyContinue
[IO.File]::WriteAllText((Join-Path $Output 'result.json'),(@{results=$results;nativeInput=$false;syntheticOnly=$true}|ConvertTo-Json -Depth 6),$utf8)
Write-Output 'ALL11 PIC3 LAUNCHER CASES PASSED; nativeInput=false'
