param(
    [ValidateSet('Prepare','Run','Review')][string]$Mode='Prepare',
    [Parameter(Mandatory=$true)][string]$Runtime,
    [Parameter(Mandatory=$true)][string]$Package,
    [string]$MainConfig,[string]$Binding,[string]$SessionEditor,[string]$Pic8Assets
)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
$Runtime=[IO.Path]::GetFullPath($Runtime)
$Package=[IO.Path]::GetFullPath($Package)
function Invoke-Pixora([string[]]$Arguments) {
    $ErrorActionPreference='Continue'
    $text=& $Runtime @Arguments 2>&1 | Out-String
    $code=$LASTEXITCODE
    $ErrorActionPreference='Stop'
    if($text){Write-Host $text}
    Write-Host ('Pixora exit code: '+$code+'; arguments: '+($Arguments -join ' '))
    if($code -ne 0){throw "Pixora failed ($code). No automatic retry."}
}
try {
    if($Mode -eq 'Prepare') {
        if(-not $MainConfig -or -not $Binding -or -not $SessionEditor -or -not $Pic8Assets){throw 'Prepare requires MainConfig, Binding, SessionEditor and Pic8Assets.'}
        Invoke-Pixora @('--texel-session-package',$MainConfig,$Binding,$SessionEditor,$Pic8Assets,$Package)
        Write-Host 'Prepared without input. Main config is unchanged. Product_1: 107 strokes, exact rect 465/151/1509/1192, requested/effective Max Speed.'
        exit 0
    }
    $data=Join-Path $Package 'Data'
    if(-not(Test-Path -LiteralPath (Join-Path $Package 'package.json'))){throw 'Prepare the package first.'}
    if($Mode -eq 'Run') {
        if(Test-Path -LiteralPath (Join-Path $data 'last-run')){throw 'A run already exists. Retain it and use Review; no automatic retry/resume.'}
        Write-Host 'Only you execute START. Open the same Rust editor and clear Canvas. Keep view, DPI, window and controls unchanged.'
        Write-Host ('In Pixora load '+(Join-Path $Package 'Product_1.png')+'; verify the texel preview, then press Start once.')
        Write-Host 'The run must retain 2 native Canvas frames and 1 fresh full view. ESC stops painting. This is not physical certification.'
        Invoke-Pixora @('--data-folder',$data)
        exit 0
    }
    $runs=@(Get-ChildItem -LiteralPath (Join-Path $data 'last-run') -Directory | Where-Object {Test-Path -LiteralPath (Join-Path $_.FullName 'experimental-texel-plan.json')})
    if($runs.Count -ne 1){throw 'Review requires exactly one retained texel run.'}
    $review=Join-Path $Package ('Analysis_Product1_'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
    Invoke-Pixora @('--texel-product-review',$runs[0].FullName,$review)
    Write-Host ('Review: '+$review+'; PREDICTIONS_MET never means physicalQualityCertified.')
} catch {Write-Host $_.Exception.Message;exit 1}
