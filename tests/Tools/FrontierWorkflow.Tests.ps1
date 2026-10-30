param([Parameter(Mandatory=$true)][string]$WorkflowPath,[Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
. $WorkflowPath
if(Test-Path -LiteralPath $Output) { throw 'Use a new fixture output directory' }
New-Item -ItemType Directory -Path $Output | Out-Null
$script:passed=0
$script:assertions=0
$script:results=New-Object 'System.Collections.Generic.List[object]'
function Assert([bool]$value,[string]$message) { $script:assertions++; if(!$value) { throw $message } }
function Set-State([string]$folder,[string]$state) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    if($state -eq 'New') { $status=@{status='NOT_STARTED'} }
    else {
        $status=@{result=@{Status=if($state -eq 'Complete'){'DATA_COLLECTED'}else{'INCOMPLETE'}}}
        [IO.File]::WriteAllText((Join-Path $folder 'attempted.json'),'{}')
        if($state -eq 'Complete') { [IO.File]::WriteAllText((Join-Path $folder 'started.json'),'{}') }
    }
    [IO.File]::WriteAllText((Join-Path $folder 'series-status.json'),($status|ConvertTo-Json -Depth 5))
}
function New-Trial([string]$name,[string]$a='New',[string]$b='New') {
    $script:trial=@{Pair=(Join-Path $Output $name);Events=(New-Object 'System.Collections.Generic.List[string]');
        RejectConfirm='';FailPreflight='';FailCollect='';FailVerify='';FailSessionAt=0;SessionCalls=0;LeaveNew='';NonBoolean=$false}
    Set-State (Join-Path $trial.Pair 'ascending') $a
    Set-State (Join-Path $trial.Pair 'descending') $b
    return @{
        Preflight={param($directory) $direction=Split-Path $directory -Leaf; $trial.Events.Add('preflight:'+ $direction);if($trial.FailPreflight -eq $direction){throw 'fixture preflight failure'}}
        Verify={param($directory) $direction=Split-Path $directory -Leaf; $trial.Events.Add('verify:'+ $direction);if($trial.FailVerify -eq $direction){throw 'fixture verification failure'}}
        Collect={param($directory) $direction=Split-Path $directory -Leaf; $trial.Events.Add('collect:'+ $direction);if($trial.FailCollect -eq $direction){Set-State $directory 'Incomplete';throw 'fixture collection failure'};if($trial.LeaveNew -ne $direction){Set-State $directory 'Complete'}}
        ConfirmClean={param($direction) $trial.Events.Add('confirm:'+ $direction);if($trial.NonBoolean){return 'yes'};return ($trial.RejectConfirm -ne $direction)}
        EnsureSession={param($directory) $trial.SessionCalls++;$trial.Events.Add('session');if($trial.FailSessionAt -eq $trial.SessionCalls){throw 'fixture process ended'}}
        Analyze={$trial.Events.Add('analyze')}
        Notify={param($message) $trial.Events.Add('notify')}
    }
}
function Expect-Failure([scriptblock]$action) { $failed=$false;try{& $action}catch{$failed=$true};Assert $failed 'Expected refusal did not occur' }
function No-Collection { Assert (@($trial.Events|Where-Object {$_ -like 'collect:*'}).Count -eq 0) 'Unexpected fake collection' }
function Case([string]$name,[scriptblock]$test) { & $test;$script:passed++;$results.Add(@{case=$name;passed=$true}) }

Case 'both conditions in order with explicit two confirmations' {
    $ops=New-Trial 'complete';Invoke-FrontierWorkflow 'pair' $trial.Pair $ops
    $expected='preflight:ascending,preflight:descending,confirm:ascending,collect:ascending,verify:ascending,session,confirm:descending,session,collect:descending,verify:descending,analyze,notify'
    Assert (($trial.Events -join ',') -ceq $expected) 'Two-condition action order differs'
}
Case 'check cannot paint or request confirmation' {
    $ops=New-Trial 'check';Invoke-FrontierWorkflow 'check' $trial.Pair $ops
    Assert (($trial.Events -join ',') -ceq 'preflight:ascending,preflight:descending,notify') 'Check triggered another action'
}
Case 'cancel first confirmation' {
    $ops=New-Trial 'cancel-first';$trial.RejectConfirm='ascending';Expect-Failure {Invoke-FrontierWorkflow 'pair' $trial.Pair $ops};No-Collection
}
Case 'cancel second preserves first and allows explicit later continuation' {
    $ops=New-Trial 'cancel-second';$trial.RejectConfirm='descending';Expect-Failure {Invoke-FrontierWorkflow 'pair' $trial.Pair $ops}
    Assert ((Get-FrontierConditionState (Join-Path $trial.Pair 'ascending')) -eq 'Complete') 'First half lost'
    Assert ((Get-FrontierConditionState (Join-Path $trial.Pair 'descending')) -eq 'New') 'Second half consumed on cancellation'
    $trial.Events.Clear();$trial.RejectConfirm='';Invoke-FrontierWorkflow 'pair' $trial.Pair $ops
    Assert (!$trial.Events.Contains('collect:ascending')) 'First half repainted on continuation'
    Assert ($trial.Events.Contains('collect:descending')) 'Explicit continuation did not complete second half'
}
Case 'process ended before second prompt' {
    $ops=New-Trial 'ended-before' 'Complete';$trial.FailSessionAt=1;Expect-Failure {Invoke-FrontierWorkflow 'pair' $trial.Pair $ops};No-Collection
    Assert (!$trial.Events.Contains('confirm:descending')) 'Asked to clear after known session end'
}
Case 'process ended during second prompt' {
    $ops=New-Trial 'ended-after' 'Complete';$trial.FailSessionAt=2;Expect-Failure {Invoke-FrontierWorkflow 'pair' $trial.Pair $ops};No-Collection
    Assert ($trial.Events.Contains('confirm:descending')) 'Post-prompt session gate not exercised'
}
Case 'broken second protocol refuses before first paint' {
    $ops=New-Trial 'bad-protocol';$trial.FailPreflight='descending';Expect-Failure {Invoke-FrontierWorkflow 'pair' $trial.Pair $ops};No-Collection
}
Case 'first failed capture cannot advance or retry' {
    $ops=New-Trial 'bad-first';$trial.FailCollect='ascending';Expect-Failure {Invoke-FrontierWorkflow 'pair' $trial.Pair $ops}
    Assert (!$trial.Events.Contains('collect:descending')) 'Failed first advanced'
    $trial.Events.Clear();Expect-Failure {Invoke-FrontierWorkflow 'pair' $trial.Pair $ops};No-Collection
}
Case 'first verification failure cannot advance' {
    $ops=New-Trial 'bad-verify';$trial.FailVerify='ascending';Expect-Failure {Invoke-FrontierWorkflow 'pair' $trial.Pair $ops}
    Assert (!$trial.Events.Contains('collect:descending')) 'Unverified first advanced'
}
Case 'second failed capture cannot publish model or retry' {
    $ops=New-Trial 'bad-second';$trial.FailCollect='descending';Expect-Failure {Invoke-FrontierWorkflow 'pair' $trial.Pair $ops}
    Assert (!$trial.Events.Contains('analyze')) 'Incomplete second published model'
    $trial.Events.Clear();Expect-Failure {Invoke-FrontierWorkflow 'pair' $trial.Pair $ops};No-Collection
}
Case 'completed pair is offline only even after game closes' {
    $ops=New-Trial 'already-complete' 'Complete' 'Complete';$trial.FailSessionAt=1;Invoke-FrontierWorkflow 'pair' $trial.Pair $ops;No-Collection
    Assert ($trial.Events.Contains('analyze') -and !$trial.Events.Contains('session')) 'Completed pair needs live game'
}
Case 'legacy descending entry cannot paint before first complete' {
    $ops=New-Trial 'legacy-wrong-order';Expect-Failure {Invoke-FrontierWorkflow 'descending' $trial.Pair $ops};No-Collection
}
Case 'legacy first entry cannot repaint completed first' {
    $ops=New-Trial 'legacy-repaint' 'Complete';Expect-Failure {Invoke-FrontierWorkflow 'ascending' $trial.Pair $ops};No-Collection
}
Case 'analysis of incomplete pair cannot paint' {
    $ops=New-Trial 'analysis-incomplete' 'Complete';Expect-Failure {Invoke-FrontierWorkflow 'review' $trial.Pair $ops};No-Collection
    Assert (!$trial.Events.Contains('analyze')) 'Incomplete analysis started'
}
Case 'nonboolean confirmation fails closed' {
    $ops=New-Trial 'nonboolean';$trial.NonBoolean=$true;Expect-Failure {Invoke-FrontierWorkflow 'pair' $trial.Pair $ops};No-Collection
}
Case 'run success without completed status cannot advance' {
    $ops=New-Trial 'false-success';$trial.LeaveNew='ascending';Expect-Failure {Invoke-FrontierWorkflow 'pair' $trial.Pair $ops}
    Assert (!$trial.Events.Contains('collect:descending')) 'Missing completion advanced'
}
Case 'completion label without both start markers fails closed' {
    $ops=New-Trial 'missing-marker' 'Complete';[IO.File]::Delete((Join-Path $trial.Pair 'ascending/started.json'))
    Expect-Failure {Invoke-FrontierWorkflow 'pair' $trial.Pair $ops};No-Collection
}
Case 'invalid state JSON fails closed' {
    $ops=New-Trial 'invalid-json';[IO.File]::WriteAllText((Join-Path $trial.Pair 'ascending/series-status.json'),'{')
    Expect-Failure {Invoke-FrontierWorkflow 'pair' $trial.Pair $ops};No-Collection
}
Case 'new first with completed second fails closed' {
    $ops=New-Trial 'reversed' 'New' 'Complete';Expect-Failure {Invoke-FrontierWorkflow 'pair' $trial.Pair $ops};No-Collection
}
Case 'real process liveness and creation-time guard are read only' {
    $folder=Join-Path $Output 'process-check';New-Item -ItemType Directory -Path $folder | Out-Null
    $live=@{windowProcessId=$PID;createdUtc=[DateTimeOffset]::UtcNow.ToString('O')}
    [IO.File]::WriteAllText((Join-Path $folder 'live-context.json'),($live|ConvertTo-Json));Assert-FrontierRustSession $folder
    $live.createdUtc='2000-01-01T00:00:00Z';[IO.File]::WriteAllText((Join-Path $folder 'live-context.json'),($live|ConvertTo-Json))
    Expect-Failure {Assert-FrontierRustSession $folder}
    $live.windowProcessId=[int]::MaxValue;$live.createdUtc=[DateTimeOffset]::UtcNow.ToString('O');[IO.File]::WriteAllText((Join-Path $folder 'live-context.json'),($live|ConvertTo-Json))
    Expect-Failure {Assert-FrontierRustSession $folder}
}
$receipt=@{cases=$script:passed;assertions=$script:assertions;results=$results;nativeInput=$false;physicalQualityCertified=$false;engine=$PSVersionTable.PSVersion.ToString();workflowSha256=(Get-FileHash -LiteralPath $WorkflowPath -Algorithm SHA256).Hash.ToLowerInvariant()}
[IO.File]::WriteAllText((Join-Path $Output 'result.json'),($receipt|ConvertTo-Json -Depth 8))
Write-Output "ALL $script:passed FRONTIER WORKFLOW CASES PASSED ($script:assertions assertions); nativeInput=false"
