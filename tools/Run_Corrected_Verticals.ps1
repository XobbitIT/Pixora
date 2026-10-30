[CmdletBinding()]
param([switch]$PreflightOnly)
$ErrorActionPreference='Stop'
$resultCode=1
$logDirectory=Join-Path $PSScriptRoot ('Launch_Log_'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'_'+[Guid]::NewGuid().ToString('N').Substring(0,8))
try {
    New-Item -ItemType Directory -Path $logDirectory | Out-Null
    $exe=Join-Path $PSScriptRoot 'Pixora.exe'
    $pilot=Join-Path $PSScriptRoot 'Pilot'
    if(!(Test-Path -LiteralPath $exe -PathType Leaf)){throw 'Pixora.exe is missing. Extract the entire portable archive first.'}
    $arguments=@('--standard-band-inverse','preflight',('"'+$pilot+'"'))
    $preflight=Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logDirectory 'preflight.stdout.log') -RedirectStandardError (Join-Path $logDirectory 'preflight.stderr.log')
    if($preflight.ExitCode -ne 0){
        Get-Content -LiteralPath (Join-Path $logDirectory 'preflight.stderr.log') -Encoding UTF8
        throw 'Pilot preflight failed. No painting was started. See the log directory below.'
    }
    Write-Host 'Portable pilot validated. Two vertical bands / 20 strokes.'
    if($PreflightOnly){
        Write-Host 'Preflight only: no native input or painting.'
        $resultCode=0
    } else {
        Write-Host 'Return to the Rust editor within 10 seconds. Keep the canvas clean and geometry unchanged.'
        # This is the user's explicit interactive launch, not a background helper.
        $arguments=@('--standard-band-inverse','run',('"'+$pilot+'"'))
        $painting=Start-Process -FilePath $exe -ArgumentList $arguments -Wait -PassThru -WindowStyle Normal -RedirectStandardOutput (Join-Path $logDirectory 'run.stdout.log') -RedirectStandardError (Join-Path $logDirectory 'run.stderr.log')
        $resultCode=$painting.ExitCode
        if($resultCode -ne 0){Get-Content -LiteralPath (Join-Path $logDirectory 'run.stderr.log') -Encoding UTF8;Write-Host 'Pilot stopped. Keep the Pilot folder and logs.'}
    }
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    if(Test-Path -LiteralPath $logDirectory){$_ | Out-String | Set-Content -LiteralPath (Join-Path $logDirectory 'launcher-error.log') -Encoding UTF8}
    $resultCode=1
} finally {
    Write-Host ('Logs: '+$logDirectory)
}
exit $resultCode
