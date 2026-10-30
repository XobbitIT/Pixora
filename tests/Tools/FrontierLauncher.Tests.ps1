param([Parameter(Mandatory=$true)][string]$Tools,[Parameter(Mandatory=$true)][string]$FixtureBuild,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $Output) { throw 'New fixture directory required' }
New-Item -ItemType Directory -Path $Output | Out-Null
$env:PIXORA_FRONTIER_FIXTURE_PID="$PID"
$utf8=New-Object System.Text.UTF8Encoding($true)
$cases=@(
    @{Name='complete';Input="1`n2`n";Fail='';Actions='preflight:ascending,preflight:descending,run-session:ascending,verify:ascending,run-session:descending,verify:descending,review:Frontier_Pair';Code=0},
    @{Name='eof';Input='';Fail='';Actions='preflight:ascending,preflight:descending';Code=1},
    @{Name='cancel-second';Input="1`n`n";Fail='';Actions='preflight:ascending,preflight:descending,run-session:ascending,verify:ascending';Code=1},
    @{Name='bad-second-protocol';Input="1`n2`n";Fail='preflight:descending';Actions='preflight:ascending,preflight:descending';Code=1},
    @{Name='bad-first-verify';Input="1`n2`n";Fail='verify:ascending';Actions='preflight:ascending,preflight:descending,run-session:ascending,verify:ascending';Code=1},
    @{Name='bad-second-run';Input="1`n2`n";Fail='run-session:descending';Actions='preflight:ascending,preflight:descending,run-session:ascending,verify:ascending,run-session:descending';Code=1}
)
$results=@()
foreach($case in $cases) {
    $folder=Join-Path $Output ($case.Name+' space '+[char]0x3A9)
    New-Item -ItemType Directory -Path $folder | Out-Null
    foreach($name in @('Frontier_Workflow.ps1','Discrete_Edges.ps1')) { [IO.File]::WriteAllText((Join-Path $folder $name),(Get-Content -LiteralPath (Join-Path $Tools $name) -Raw -Encoding UTF8),$utf8) }
    foreach($file in (Get-ChildItem -LiteralPath $FixtureBuild -File)) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $folder $file.Name) }
    Copy-Item -LiteralPath (Join-Path $FixtureBuild 'FrontierLauncherFixture.exe') -Destination (Join-Path $folder 'Pixora.exe')
    [IO.File]::WriteAllText((Join-Path $folder 'OFFLINE_FIXTURE_ONLY'),'No native input implementation')
    $pair=Join-Path $folder 'Frontier_Pair';New-Item -ItemType Directory -Path $pair | Out-Null
    [IO.File]::WriteAllText((Join-Path $pair 'pair.json'),'{}')
    foreach($direction in @('ascending','descending')) {
        $condition=Join-Path $pair $direction;New-Item -ItemType Directory -Path $condition | Out-Null
        [IO.File]::WriteAllText((Join-Path $condition 'protocol.json'),'{}')
        [IO.File]::WriteAllText((Join-Path $condition 'series-status.json'),'{"status":"NOT_STARTED"}')
    }
    $inputFile=Join-Path $folder 'input.txt';[IO.File]::WriteAllText($inputFile,$case.Input)
    $env:PIXORA_FRONTIER_FIXTURE_FAIL=$case.Fail
    $words='-NoProfile -ExecutionPolicy Bypass -File "'+(Join-Path $folder 'Discrete_Edges.ps1')+'" -Mode pair'
    $job=Start-Process -FilePath 'powershell.exe' -ArgumentList $words -WorkingDirectory $folder -WindowStyle Hidden -Wait -PassThru -RedirectStandardInput $inputFile -RedirectStandardOutput (Join-Path $folder 'stdout.log') -RedirectStandardError (Join-Path $folder 'stderr.log')
    if($job.ExitCode -ne $case.Code) { throw "Wrong exit code for $($case.Name): $($job.ExitCode)" }
    $events=Get-Content -LiteralPath (Join-Path $folder 'fixture-events.jsonl') | ForEach-Object { ConvertFrom-Json $_ }
    $actions=($events|ForEach-Object {$_.action+':'+$_.direction}) -join ','
    if($actions -cne $case.Actions -or @($events|Where-Object {$_.nativeInput -ne $false}).Count -ne 0) { throw "Wrong action sequence: $($case.Name): $actions" }
    $analyses=@(Get-ChildItem -LiteralPath $folder -Directory -Filter 'Analysis_*')
    if(($analyses.Count -eq 1) -ne ($case.Code -eq 0)) { throw "Wrong analysis publication for $($case.Name)" }
    $results+=@{case=$case.Name;passed=$true;exitCode=$job.ExitCode;actions=$actions}
}
Remove-Item Env:PIXORA_FRONTIER_FIXTURE_PID,Env:PIXORA_FRONTIER_FIXTURE_FAIL
$receipt=@{cases=$cases.Count;results=$results;nativeInput=$false;syntheticOnly=$true;physicalQualityCertified=$false;engine=$PSVersionTable.PSVersion.ToString();launcherSha256=(Get-FileHash -LiteralPath (Join-Path $Tools 'Discrete_Edges.ps1') -Algorithm SHA256).Hash.ToLowerInvariant();workflowSha256=(Get-FileHash -LiteralPath (Join-Path $Tools 'Frontier_Workflow.ps1') -Algorithm SHA256).Hash.ToLowerInvariant()}
[IO.File]::WriteAllText((Join-Path $Output 'result.json'),($receipt|ConvertTo-Json -Depth 6))
Write-Output "ALL $($cases.Count) FRONTIER LAUNCHER INTEGRATION CASES PASSED; nativeInput=false"
