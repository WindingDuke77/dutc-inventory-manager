# DUTC Inventory build script
# Merges src\*.cs (in filename order) into DutcInventory_paste.cs for the
# Space Engineers Programmable Block. The config module (00_*) keeps its
# comments; all other modules get comments and blank lines stripped.
# Run:  powershell -ExecutionPolicy Bypass -File build.ps1

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root "src"
$outFile = Join-Path $root "DutcInventory_paste.cs"

$files = Get-ChildItem $srcDir -Filter *.cs | Sort-Object Name
if ($files.Count -eq 0) { Write-Error "No .cs files found in $srcDir"; exit 1 }

$out = New-Object System.Collections.Generic.List[string]
foreach ($f in $files) {
    $lines = Get-Content $f.FullName
    if ($f.Name -like "00_*") {
        foreach ($l in $lines) { $out.Add($l) }
        continue
    }
    foreach ($l in $lines) {
        $t = $l.Trim()
        if ($t -eq "") { continue }
        if ($t.StartsWith("//")) { continue }
        $out.Add($l)
    }
}
$out | Out-File $outFile -Encoding utf8

$raw = Get-Content $outFile -Raw
$openB = ($raw.ToCharArray() | Where-Object { $_ -eq '{' }).Count
$closeB = ($raw.ToCharArray() | Where-Object { $_ -eq '}' }).Count
Write-Host "Characters : $($raw.Length) / 100000 (PB limit)"
Write-Host "Braces     : $openB / $closeB"
if ($raw.Length -gt 100000) { Write-Host "TOO BIG for the Programmable Block!" -ForegroundColor Red }
elseif ($openB -ne $closeB) { Write-Host "WARNING: unbalanced braces!" -ForegroundColor Yellow }
else { Write-Host "OK - paste DutcInventory_paste.cs into the Programmable Block." -ForegroundColor Green }
try { Set-Clipboard -Value $raw; Write-Host "Copied to clipboard." -ForegroundColor Green } catch { }
