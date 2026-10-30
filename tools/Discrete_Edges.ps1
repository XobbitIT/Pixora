param([ValidateSet('check','pair','ascending','descending','review')][string]$Mode='check')
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false)
$taskRoot=$PSScriptRoot
$program=Join-Path $taskRoot 'Pixora.exe'
$pair=Join-Path $taskRoot 'Frontier_Pair'
$logRoot=Join-Path $taskRoot ('Logs_'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'_'+[Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $logRoot | Out-Null
function Invoke-Offline([string[]]$cliWords,[string]$tag) {
    $quoted=($cliWords | ForEach-Object { '"'+$_+'"' }) -join ' '
    $outFile=Join-Path $logRoot ($tag+'.stdout.log')
    $errFile=Join-Path $logRoot ($tag+'.stderr.log')
    $job=Start-Process -FilePath $program -ArgumentList $quoted -WorkingDirectory $taskRoot -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput $outFile -RedirectStandardError $errFile
    if($job.ExitCode -ne 0) {
        if(Test-Path -LiteralPath $errFile) { Get-Content -LiteralPath $errFile -Raw -Encoding UTF8 | Write-Host }
        throw "Перевірку не завершено. Логи: $logRoot"
    }
}
function Analyze-Pair {
    $destination=Join-Path $taskRoot ('Analysis_'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'_'+[Guid]::NewGuid().ToString('N').Substring(0,8))
    Invoke-Offline @('--standard-discrete-band','review',$pair,$destination) 'analysis'
    Write-Host "Модель збережено: $destination"
}
try {
    . (Join-Path $taskRoot 'Frontier_Workflow.ps1')
    if(!(Test-Path -LiteralPath $program) -or !(Test-Path -LiteralPath (Join-Path $pair 'pair.json'))) { throw 'Розпакуй увесь архів у нову папку.' }
    $operations=@{
        Preflight={param($directory) Invoke-Offline @('--standard-discrete-band','preflight',$directory) ('preflight-'+(Split-Path $directory -Leaf))}
        Verify={param($directory) Invoke-Offline @('--standard-discrete-band','verify',$directory) ('verify-'+(Split-Path $directory -Leaf))}
        EnsureSession={param($directory) Assert-FrontierRustSession $directory}
        ConfirmClean={
            param($direction)
            if($direction -eq 'ascending') {
                Write-Host 'КРОК 1 З 2. Потрібна відкрита гра Rust. Очисти полотно кошиком.'
                Write-Host 'Після першого напрямку ця сама консоль попросить очистити полотно для другого. Rust між ними не закривай.'
                Write-Host 'Це підтверджує нове захоплення рамки поточної сесії. Точні координати виділення залишаються незмінними.'
                Write-Host 'Після відліку збір перевірить два кадри, не відкриваючи додаткових вікон поверх Rust.'
                $token='1'
            } else {
                Write-Host 'КРОК 2 З 2. Перший напрямок уже збережено. НЕ ЗАКРИВАЙ RUST.'
                Write-Host 'Повернись у Rust, очисти полотно кошиком, потім повернись у цю консоль.'
                $token='2'
            }
            Write-Host 'Після підтвердження є 10 секунд, щоб повернутися до редактора. ESC у грі зупиняє збір.'
            $answer=Read-Host "Коли чисте полотно готове, введи $token і натисни Enter. Інша відповідь скасовує"
            return ($answer -ceq $token)
        }
        Collect={
            param($directory)
            $tag='run-'+(Split-Path $directory -Leaf)
            $runWords='--standard-discrete-band run-session "'+$directory+'"'
            $job=Start-Process -FilePath $program -ArgumentList $runWords -WorkingDirectory $taskRoot -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $logRoot ($tag+'.stdout.log')) -RedirectStandardError (Join-Path $logRoot ($tag+'.stderr.log'))
            if($job.ExitCode -ne 0) { throw "Збір не завершено. Збережи Frontier_Pair і логи: $logRoot" }
        }
        Analyze={Analyze-Pair}
        Notify={param($message) Write-Host $message}
    }
    Invoke-FrontierWorkflow $Mode $pair $operations
    exit 0
} catch {
    Write-Host $_.Exception.Message
    Write-Host "Логи: $logRoot"
    exit 1
}
