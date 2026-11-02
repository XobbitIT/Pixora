param([ValidateSet('Run','Check','Review')][string]$Mode='Run',[string]$Package=$PSScriptRoot)
# Step 3 launcher: Product_1 from the main config through the binding confirmed by Session_Calib_1 of THIS package.
# It never paints by itself: the user presses Start in the normal Pixora UI. No retry/resume of a retained run.
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$Package=[IO.Path]::GetFullPath($Package)
$runtime=Join-Path $Package 'Pixora.exe'
$inner=Join-Path $Package 'SessionProduct1_Experiment.ps1'
$product=Join-Path $Package 'Product1_Session'
$editor=Join-Path $Package 'Session1_Experiment\session-editor.json'
$mainConfig=Join-Path $env:LOCALAPPDATA 'Pixora\config-v3.json'
$pic8=Join-Path $Package 'Frozen_Pic8'
function Invoke-Inner([string[]]$Arguments){
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $inner @Arguments
    if($LASTEXITCODE -ne 0){throw ('Крок '+$Arguments[1]+' не пройшов (код '+$LASTEXITCODE+'). Ввід не повторюється автоматично.')}
}
function Find-Binding {
    $found=@()
    foreach($d in @(Get-ChildItem -LiteralPath $Package -Directory -Filter 'Analysis_Session1_*' -ErrorAction SilentlyContinue)){
        $f=Join-Path $d.FullName 'session-binding.json'
        if(-not(Test-Path -LiteralPath $f)){continue}
        $b=Get-Content -LiteralPath $f -Raw -Encoding UTF8 | ConvertFrom-Json
        if($b.status -eq 'SESSION_TRANSFER_CONFIRMED' -and $b.bindingActive -eq $true){$found+=$f}
    }
    $ids=@($found | ForEach-Object {(Get-Content -LiteralPath $_ -Raw -Encoding UTF8 | ConvertFrom-Json).liveBindingId} | Select-Object -Unique)
    if($ids.Count -eq 0){throw 'Немає підтвердженого binding цього пакета. Спершу Start_Session1_Test.cmd має дати SESSION_TRANSFER_CONFIRMED.'}
    if($ids.Count -gt 1){throw 'Знайдено кілька різних підтверджених binding; автоматично не обираю. Деталі в Analysis_Session1_*.'}
    return $found[0]
}
try {
    if(-not(Test-Path -LiteralPath $runtime -PathType Leaf)){throw 'Не знайдено Pixora.exe у папці пакета.'}
    $prepared=Test-Path -LiteralPath (Join-Path $product 'package.json')
    $retained=Test-Path -LiteralPath (Join-Path $product 'Data\last-run')
    if($Mode -eq 'Run' -and $retained){Write-Host 'Прогін Product_1 уже збережено. Нове малювання вимкнене; виконую лише огляд.';$Mode='Review'}
    if($Mode -eq 'Review'){Invoke-Inner @('-Mode','Review','-Runtime',$runtime,'-Package',$product);exit 0}
    $binding=Find-Binding
    Write-Host ('Binding: '+$binding)
    if(-not $prepared){
        if(-not(Test-Path -LiteralPath $editor)){throw 'Немає Session1_Experiment\session-editor.json цього пакета.'}
        Invoke-Inner @('-Mode','Prepare','-Runtime',$runtime,'-Package',$product,'-MainConfig',$mainConfig,'-Binding',$binding,'-SessionEditor',$editor,'-Pic8Assets',$pic8)
    } else {Write-Host 'Пакет Product_1 уже підготовлено; повторно не готую.'}
    if($Mode -eq 'Check'){Write-Host 'Підготовку перевірено. Rust-ввід не запускався.';exit 0}
    Write-Host ''
    Write-Host 'Product_1 з основного конфігу: 107 коротких горизонталей, полотно 465/151/1509/1192, Max Speed.'
    Write-Host '1. У Rust очисти полотно кошиком (після калібрування на ньому 3 лінії). Вид, вікно, DPI і виділення не змінюй.'
    Write-Host '2. Відкриється Pixora. Завантаж Product_1.png з папки Product1_Session, перевір прев''ю і натисни «Почати» один раз.'
    Write-Host '3. Після завершення закрий Pixora — огляд запуститься сам. ESC зупиняє малювання.'
    Write-Host 'Натисни Enter, щоб відкрити Pixora. Будь-який інший ввід скасовує.'
    $answer=[Console]::ReadLine()
    if($null -eq $answer -or $answer -ne ''){Write-Host 'Скасовано до вводу.';exit 2}
    # A failed UI exit must not hide a run that was already saved. Never retry
    # input; inspect retained evidence before deciding whether Review can run.
    try {Invoke-Inner @('-Mode','Run','-Runtime',$runtime,'-Package',$product)}
    catch {Write-Host ('UI Run: '+$_.Exception.Message)}
    if(-not(Test-Path -LiteralPath (Join-Path $product 'Data\last-run'))){Write-Host 'Збереженого прогону немає (START не натиснуто). Можна запустити ще раз.';exit 1}
    Invoke-Inner @('-Mode','Review','-Runtime',$runtime,'-Package',$product)
    exit 0
} catch {Write-Host $_.Exception.Message;exit 1}
