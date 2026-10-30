# Pure workflow. Tests supply recording fakes, never the product's run command.
function Get-FrontierConditionState([string]$Directory) {
    $statusPath=Join-Path $Directory 'series-status.json'
    if(!(Test-Path -LiteralPath $statusPath -PathType Leaf)) { throw 'Немає стану вимірювання. Розпакуй увесь пакет у нову папку.' }
    $state=Get-Content -LiteralPath $statusPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $attempted=Test-Path -LiteralPath (Join-Path $Directory 'attempted.json')
    $started=Test-Path -LiteralPath (Join-Path $Directory 'started.json')
    if($state.PSObject.Properties['status'] -and $state.status -ceq 'NOT_STARTED' -and !$attempted -and !$started) { return 'New' }
    if($state.PSObject.Properties['result'] -and $state.result.Status -ceq 'DATA_COLLECTED' -and $attempted -and $started) { return 'Complete' }
    throw 'Є незавершена або пошкоджена спроба. Збережи дані; автоматичне повторення заборонене. Для нового збору потрібна нова папка.'
}

function Assert-FrontierRustSession([string]$AscendingDirectory) {
    $live=Get-Content -LiteralPath (Join-Path $AscendingDirectory 'live-context.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if(!$live.PSObject.Properties['windowProcessId'] -or !$live.PSObject.Properties['createdUtc']) { throw 'Немає ідентифікатора сесії першого напрямку.' }
    $recorded=[DateTimeOffset]::Parse($live.createdUtc,[Globalization.CultureInfo]::InvariantCulture)
    $process=$null
    try {
        $process=[Diagnostics.Process]::GetProcessById([int]$live.windowProcessId)
        if($process.HasExited -or $process.StartTime.ToUniversalTime() -gt $recorded.UtcDateTime) { throw 'Process restarted' }
    } catch {
        throw 'Rust першого напрямку вже закритий або перезапущений. Збережи отримані дані. Обидва напрямки треба зібрати в одній сесії Rust з нової папки.'
    } finally { if($null -ne $process) { $process.Dispose() } }
}

function Invoke-FrontierWorkflow([string]$Mode,[string]$PairDirectory,[hashtable]$Operations) {
    foreach($name in @('Preflight','Verify','Collect','ConfirmClean','EnsureSession','Analyze','Notify')) {
        if(!$Operations.ContainsKey($name) -or $Operations[$name] -isnot [scriptblock]) { throw "Missing workflow operation: $name" }
    }
    $ascending=Join-Path $PairDirectory 'ascending'
    $descending=Join-Path $PairDirectory 'descending'
    # A damaged second half must be rejected before any first-half input.
    foreach($directory in @($ascending,$descending)) { & $Operations.Preflight $directory | Out-Null }
    if($Mode -eq 'check') { & $Operations.Notify 'Файли обох напрямків перевірено без малювання.'; return }
    if($Mode -notin @('pair','ascending','descending','review')) { throw 'Unknown frontier workflow mode' }
    $a=Get-FrontierConditionState $ascending
    $b=Get-FrontierConditionState $descending
    if($a -eq 'New' -and $b -eq 'Complete') { throw 'Порядок напрямків порушено. Збережи дані; потрібна нова папка.' }
    if($Mode -eq 'review') {
        if($a -ne 'Complete' -or $b -ne 'Complete') { throw 'Для аналізу потрібні два завершені напрямки.' }
    } elseif($Mode -eq 'ascending' -and ($a -ne 'New' -or $b -ne 'New')) {
        throw 'Перший напрямок уже запускали. Скористайся Start_Frontier_Pair.cmd; готові штрихи не повторюються.'
    } elseif($Mode -eq 'descending' -and ($a -ne 'Complete' -or $b -ne 'New')) {
        throw 'Для другого напрямку потрібен завершений перший і ще не розпочатий другий.'
    }
    if($a -eq 'New') {
        $confirmed=& $Operations.ConfirmClean 'ascending'
        if($confirmed -isnot [bool] -or !$confirmed) { throw 'Збір скасовано до вводу. Дані не перезаписуються.' }
        & $Operations.Collect $ascending | Out-Null
        & $Operations.Verify $ascending | Out-Null
        if((Get-FrontierConditionState $ascending) -ne 'Complete') { throw 'Перший напрямок не завершений; другий не запускатиметься.' }
        if($Mode -eq 'ascending') { & $Operations.Notify 'Перший напрямок збережено. Не закривай Rust: продовж із Start_Frontier_Pair.cmd після очищення полотна.'; return }
    } else { & $Operations.Verify $ascending | Out-Null }
    if($b -eq 'New') {
        # The game may close while the console waits for human confirmation.
        & $Operations.EnsureSession $ascending | Out-Null
        $confirmed=& $Operations.ConfirmClean 'descending'
        if($confirmed -isnot [bool] -or !$confirmed) { throw 'Другий напрямок не запущено. Перший збережено; не закривай Rust, якщо продовжуватимеш цю пару.' }
        & $Operations.EnsureSession $ascending | Out-Null
        & $Operations.Collect $descending | Out-Null
        & $Operations.Verify $descending | Out-Null
        if((Get-FrontierConditionState $descending) -ne 'Complete') { throw 'Другий напрямок не завершений; модель не публікується.' }
    } else { & $Operations.Verify $descending | Out-Null }
    & $Operations.Analyze | Out-Null
    & $Operations.Notify 'Обидва напрямки збережено й проаналізовано. Це вимірювання для коректора; фізичну якість ще не сертифіковано.'
}
