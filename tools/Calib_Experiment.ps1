param([ValidateSet('Run','Check','Review')][string]$Mode='Run',[string]$Package=$PSScriptRoot)
$ErrorActionPreference='Stop'
$Package=[IO.Path]::GetFullPath($Package)
$runtimeFile=Join-Path $Package 'Pixora.exe'
$experiment=Join-Path $Package 'Calib_Experiment'
$stamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$logFolder=Join-Path $Package ('Logs_Calib_'+$stamp)
New-Item -ItemType Directory -Path $logFolder | Out-Null
function Invoke-CalibCommand([string[]]$Arguments,[string]$LogName) {
    # Windows PowerShell treats redirected native stderr as error records;
    # retain them and the exit code so a failed run can still be reviewed.
    $ErrorActionPreference='Continue'
    $text=& $runtimeFile @Arguments 2>&1 | Out-String
    $code=$LASTEXITCODE
    $ErrorActionPreference='Stop'
    [IO.File]::WriteAllText((Join-Path $logFolder $LogName),$text,(New-Object Text.UTF8Encoding($true)))
    if($text){Write-Host $text}
    return $code
}
function Show-CalibStatus {
    $path=Join-Path $experiment 'series-status.json'
    if(Test-Path -LiteralPath $path){
        try{
            $saved=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
            $state=if($saved.result){$saved.result}else{$saved}
            Write-Host ('Стан спроби: '+$state.Status)
            if($state.Reason){Write-Host ('Причина: '+$state.Reason)}
            return ($state.Status -eq 'DATA_COLLECTED')
        }catch{Write-Host 'Не вдалося прочитати стан; деталі збережені в логах.'}
    }
    return $false
}
try {
    if(-not(Test-Path -LiteralPath $runtimeFile -PathType Leaf)){throw 'Не знайдено Pixora.exe у папці пакета.'}
    $retained=@('attempted.json','started.json','events.jsonl','baseline.png','final.png','session-editor.json','geometry-check.json','startup-failures.jsonl') | Where-Object {Test-Path -LiteralPath (Join-Path $experiment $_)}
    $sampleFolders=@(Get-ChildItem -LiteralPath $experiment -Directory -Filter 'sample-*' -ErrorAction SilentlyContinue)
    $previousCode=0
    if($Mode -eq 'Run' -and (@($retained).Count -gt 0 -or $sampleFolders.Count -gt 0)){
        Write-Host 'Цю спробу вже запускали. Збережені дані не змінюються; зараз виконується лише аналіз без нового малювання.'
        $previousCode=1
        if(Show-CalibStatus){$previousCode=0}
        $Mode='Review'
    }
    if($Mode -ne 'Review'){
        $code=Invoke-CalibCommand @('--standard-contour-pilot','calib-preflight',$experiment) 'preflight.log'
        if($code -ne 0){throw 'Перевірка файлів не пройшла. Ввід не починався; деталі у preflight.log. Стару спробу не перезаписуємо.'}
        if($Mode -eq 'Check'){Write-Host 'Файли перевірено. Rust-ввід не запускався.';exit 0}
        Write-Host 'Калібрування рядків: 40 штрихів — 16 коротких горизонталей і чорна картинка 40×15 (24 штрихи).'
        Write-Host 'Відкрий той самий редактор Rust. Очисти полотно кошиком; залиш Interval 0.01 та Opacity 1.00.'
        Write-Host 'Не змінюй вид таблички, розмір вікна, DPI або виділення. Після підтвердження є 10 секунд повернутися в Rust.'
        Write-Host 'Коли чисте полотно готове, натисни Enter. Q або кінець вводу скасовує запуск.'
        $answer=[Console]::ReadLine()
        if($null -eq $answer -or $answer -ne ''){Write-Host 'Скасовано до вводу; дані не перезаписано.';exit 2}
        Write-Host 'Повернись у Rust і відпусти кнопки миші та клавіші. ESC зупиняє спробу.'
        $runCode=Invoke-CalibCommand @('--standard-contour-pilot','calib-run',$experiment) 'run.log'
        $null=Show-CalibStatus
    } else {$runCode=$previousCode}
    $review=Join-Path $Package ('Analysis_Calib_'+$stamp)
    $reviewCode=Invoke-CalibCommand @('--standard-contour-pilot','calib-review',$experiment,$review) 'review.log'
    Write-Host ('Звіт: '+$review)
    if($runCode -ne 0 -or $reviewCode -ne 0){Write-Host 'Спроба неповна. Збережено доступні кадри й логи; повторне малювання цього каталогу вимкнене.';exit 1}
    $summary=Get-Content -LiteralPath (Join-Path $review 'summary.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host ('Результат вимірювання: '+$summary.result.Status)
    Write-Host ('Картинка: '+$summary.picture.Status)
    Write-Host 'Це калібрування адресації; фізична якість Standard ще не сертифікована.'
    exit 0
} catch {
    [IO.File]::WriteAllText((Join-Path $logFolder 'launcher-error.log'),$_.ToString(),(New-Object Text.UTF8Encoding($true)))
    Write-Host $_.Exception.Message
    exit 1
} finally {Write-Host ('Логи: '+$logFolder)}
