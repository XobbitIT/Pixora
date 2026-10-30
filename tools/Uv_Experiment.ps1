param(
    [ValidateSet('Run','Check')][string]$Mode = 'Run',
    [string]$Package = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
$Package = [System.IO.Path]::GetFullPath($Package)
$runtimeFile = Join-Path $Package 'Pixora.exe'
$experiment = Join-Path $Package 'UV_Experiment'
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$logFolder = Join-Path $Package ('Logs_UV_' + $stamp)
New-Item -ItemType Directory -Path $logFolder | Out-Null

function Invoke-UvCommand([string[]]$Arguments,[string]$LogName) {
    $text = & $runtimeFile @Arguments 2>&1 | Out-String
    $code = $LASTEXITCODE
    $text | Set-Content -LiteralPath (Join-Path $logFolder $LogName) -Encoding UTF8
    if ($text) { Write-Host $text }
    return $code
}

try {
    if (-not (Test-Path -LiteralPath $runtimeFile -PathType Leaf)) { throw 'Не знайдено Pixora.exe. Відкрий файл із повної папки пакета.' }
    $code = Invoke-UvCommand @('--standard-contour-pilot','uv-preflight',$experiment) 'preflight.log'
    if ($code -ne 0) { throw 'Перевірка файлів не пройшла. Цю спробу вже запускали або пакет змінився. Деталі збережено у preflight.log; ввід не розпочато.' }
    if ($Mode -eq 'Check') { Write-Host 'Файли перевірено. Rust-ввід не запускався.'; exit 0 }
    Write-Host 'Один дослід: 12 чорних ліній, три рознесені групи.'
    Write-Host 'Відкрий у Rust той самий редактор і повністю очисти полотно кошиком.'
    Write-Host 'Залиш ті самі розмір вікна, масштаб, DPI, Interval 0.01 та Opacity 1.00.'
    Write-Host 'Після підтвердження є 10 секунд, щоб повернутися в редактор.'
    $answer = Read-Host 'Натисни Enter, коли чисте полотно готове; введи Q для скасування'
    if ($answer -ne '') { Write-Host 'Скасовано до вводу. Дані не перезаписані.'; exit 2 }
    Write-Host 'Починаю відлік. Повернись у Rust і відпусти кнопки миші та клавіші.'
    $runCode = Invoke-UvCommand @('--standard-contour-pilot','uv-run',$experiment) 'run.log'
    $review = Join-Path $Package ('Analysis_UV_' + $stamp)
    $reviewCode = Invoke-UvCommand @('--standard-contour-pilot','uv-review',$experiment,$review) 'review.log'
    if ($runCode -ne 0 -or $reviewCode -ne 0) {
        Write-Host 'Спроба неповна. Повторно цей каталог не запускається. Збережено всі доступні дані та логи.'
        Write-Host ('Аналіз: ' + $review)
        exit 1
    }
    Write-Host ('Збір завершено. Звіт: ' + $review)
    Write-Host 'У edges.csv є S, кількість та знаки стрибків для всіх 24 країв. Це діагностика, не сертифікація якості.'
    exit 0
}
catch {
    $_.ToString() | Set-Content -LiteralPath (Join-Path $logFolder 'launcher-error.log') -Encoding UTF8
    Write-Host $_.Exception.Message
    exit 1
}
finally { Write-Host ('Логи: ' + $logFolder) }
