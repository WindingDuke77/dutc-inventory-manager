# DUTC Inventory build script
# Merges src\*.cs (in filename order) into DutcInventory_paste.cs for the
# Space Engineers Programmable Block. The config module (00_*) stays readable;
# all other modules are minified: comments and unneeded whitespace removed.
# Names are left alone, so the compiled program is identical to the source.
# Run:  powershell -ExecutionPolicy Bypass -File build.ps1

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root "src"
$outFile = Join-Path $root "DutcInventory_paste.cs"

$files = Get-ChildItem $srcDir -Filter *.cs | Sort-Object Name
if ($files.Count -eq 0) { Write-Error "No .cs files found in $srcDir"; exit 1 }

# "skip" = comments and whitespace; strings and chars stay whole tokens
$tokenRx = [regex]'(?s)(?<skip>//[^\n]*|/\*.*?\*/|\s+)|\$?@"(?:[^"]|"")*"|\$?"(?:[^"\\\n]|\\.)*"|''(?:[^''\\\n]|\\.)+''|[^\s"''/@$]+|.'
# neighbours that would fuse into a different token without a space
$fuses = '^([\w@$][\w@$]|\d\.|\+\+|--|&&|\|\||[=!<>+\-*/%&|^]=|<<|>>|\?\?|\?\.|::|->|=>|//|/\*|\*/)$'

function Minify([string]$code) {
    $sb = New-Object System.Text.StringBuilder
    $gap = $false; $lineLen = 0; $prev = ''
    foreach ($m in $tokenRx.Matches($code)) {
        if ($m.Groups['skip'].Success) { $gap = $true; continue }
        $t = $m.Value
        if ($gap -and $prev) {
            # lines stay short enough for the in-game editor
            if ($lineLen -ge 200) { [void]$sb.Append("`n"); $lineLen = 0 }
            elseif ("$prev$($t[0])" -cmatch $fuses) { [void]$sb.Append(' '); $lineLen++ }
        }
        [void]$sb.Append($t); $lineLen += $t.Length; $prev = $t[$t.Length - 1]; $gap = $false
    }
    $sb.ToString()
}

function Get-CodeOnly([string]$code) {
    $sb = New-Object System.Text.StringBuilder
    foreach ($m in $tokenRx.Matches($code)) { if (-not $m.Groups['skip'].Success) { [void]$sb.Append($m.Value) } }
    $sb.ToString()
}

$config = ""
$code = New-Object System.Text.StringBuilder
foreach ($f in $files) {
    $text = [IO.File]::ReadAllText($f.FullName)
    if ($f.Name -like "00_*") { $config += $text } else { [void]$code.Append($text).Append("`n") }
}
$source = $code.ToString()
$min = Minify $source
if ((Get-CodeOnly $source) -cne (Get-CodeOnly $min)) {
    Write-Host "FAIL: minifying changed the code!" -ForegroundColor Red
    exit 1
}
$text = ($config -replace "`r`n", "`n").TrimEnd() + "`n" + $min + "`n"
[IO.File]::WriteAllText($outFile, $text, (New-Object System.Text.UTF8Encoding($true)))

$raw = Get-Content $outFile -Raw
$openB = ($raw.ToCharArray() | Where-Object { $_ -eq '{' }).Count
$closeB = ($raw.ToCharArray() | Where-Object { $_ -eq '}' }).Count
Write-Host "Characters : $($raw.Length) / 100000 (PB limit)"
Write-Host "Braces     : $openB / $closeB"
# over the limit or unbalanced = BUILD FAILURE, not a note someone can scroll past
if ($raw.Length -gt 100000) {
    Write-Host "FAIL: $($raw.Length) chars - TOO BIG for the Programmable Block (limit 100000)!" -ForegroundColor Red
    exit 1
}
if ($openB -ne $closeB) {
    Write-Host "FAIL: unbalanced braces ($openB open / $closeB close)!" -ForegroundColor Red
    exit 1
}
if ($raw.Length -gt 95000) {
    Write-Host "WARNING: within $(100000 - $raw.Length) chars of the PB limit - time to trim." -ForegroundColor Yellow
}
Write-Host "OK - paste DutcInventory_paste.cs into the Programmable Block." -ForegroundColor Green
try { Set-Clipboard -Value $raw; Write-Host "Copied to clipboard." -ForegroundColor Green } catch { }
