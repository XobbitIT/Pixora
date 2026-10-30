param([ValidateSet('Run','Check','Review')][string]$Mode='Run',[string]$Package=$PSScriptRoot)
$ErrorActionPreference='Stop'
$Package=[IO.Path]::GetFullPath($Package)
$runtimeFile=Join-Path $Package 'Pixora.exe'
$experiment=Join-Path $Package 'Texel_Experiment'
$stamp=[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
$logFolder=Join-Path $Package ('Logs_Texel_'+$stamp)
New-Item -ItemType Directory -Path $logFolder | Out-Null
function Invoke-TexelCommand([string[]]$Arguments,[string]$LogName) {
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
try {
    if(-not(Test-Path -LiteralPath $runtimeFile -PathType Leaf)){throw 'Не знайдено Pixora.exe у папці пакета.'}
    if($Mode -ne 'Review'){
        $code=Invoke-TexelCommand @('--standard-contour-pilot','texel-preflight',$experiment) 'preflight.log'
        if($code -ne 0){throw 'Перевірка файлів не пройшла. Ввід не починався; деталі у preflight.log. Стару спробу не перезаписуємо.'}
        if($Mode -eq 'Check'){Write-Host 'Файли перевірено. Rust-ввід не запускався.';exit 0}
        Write-Host 'Один тест: 5 текселе-центрованих вертикалей і 5 контрольних, усього 10 штрихів.'
        Write-Host 'Відкрий той самий редактор Rust. Очисти полотно кошиком; залиш Interval 0.01 та Opacity 1.00.'
        Write-Host 'Не змінюй вид таблички, розмір вікна, DPI або виділення. Після підтвердження є 10 секунд повернутися в Rust.'
        Write-Host 'Коли чисте полотно готове, натисни Enter. Q або кінець вводу скасовує запуск.'
        $answer=[Console]::ReadLine()
        if($null -eq $answer -or $answer -ne ''){Write-Host 'Скасовано до вводу; дані не перезаписано.';exit 2}
        Write-Host 'Повернись у Rust і відпусти кнопки миші та клавіші. ESC зупиняє спробу.'
        $runCode=Invoke-TexelCommand @('--standard-contour-pilot','texel-run',$experiment) 'run.log'
    } else {$runCode=0}
    $review=Join-Path $Package ('Analysis_Texel_'+$stamp)
    $reviewCode=Invoke-TexelCommand @('--standard-contour-pilot','texel-review',$experiment,$review) 'review.log'
    Write-Host ('Звіт: '+$review)
    if($runCode -ne 0 -or $reviewCode -ne 0){Write-Host 'Спроба неповна. Збережено доступні кадри й логи; повторне малювання цього каталогу вимкнене.';exit 1}
    $summary=Get-Content -LiteralPath (Join-Path $review 'summary.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host ('Результат кандидата: '+$summary.result.Verdict)
    Write-Host 'Це перевірка цих вертикалей; якість усіх малюнків і Adaptive ще не сертифікована.'
    exit 0
} catch {
    [IO.File]::WriteAllText((Join-Path $logFolder 'launcher-error.log'),$_.ToString(),(New-Object Text.UTF8Encoding($true)))
    Write-Host $_.Exception.Message
    exit 1
} finally {Write-Host ('Логи: '+$logFolder)}
