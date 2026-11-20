# DPI Kalkan derleme betiği
#   powershell -ExecutionPolicy Bypass -File build.ps1            -> derler + paketler (dist\DPI-Kalkan-<sürüm>.zip)
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Preview   -> ek olarak ekran görüntüleri üretir (build\screens)
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Check     -> yalnızca derleme hatalarını kontrol eder
# Paket build\paket\ altında hazırlanır; çalışan bir kopyaya dokunulmaz.
param([switch]$Preview, [switch]$Check)
$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
# GoodbyeDPI 0.2.3rc3 resmi dosyaları (değiştirilmemiş; bkz. third_party\goodbyedpi\README.md)
$tp      = Join-Path $root 'third_party\goodbyedpi'
$build   = Join-Path $root 'build'
$dist    = Join-Path $root 'dist'
$version = '2.2.0'
$appDir  = Join-Path $build "paket\DPI-Kalkan-$version"

$csc = Get-ChildItem "$env:ProgramFiles\dotnet\sdk\*\Roslyn\bincore\csc.dll" -ErrorAction SilentlyContinue |
       Sort-Object FullName | Select-Object -Last 1
if (-not $csc) { throw '.NET SDK bulunamadı (csc.dll). https://dot.net adresinden .NET SDK kurun.' }
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path $fw)) { $fw = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319" }

New-Item -ItemType Directory -Force $build, $dist | Out-Null
$refs = 'mscorlib','System','System.Core','System.Drawing','System.Windows.Forms','System.Xml',
        'System.ServiceProcess','System.Management','System.Security' | ForEach-Object { "-r:`"$fw\$_.dll`"" }
$src  = Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs | ForEach-Object { "`"$($_.FullName)`"" }

function Invoke-Csc([string]$out, [string[]]$extra) {
    $rsp = Join-Path $build 'csc.rsp'
    $lines = @('-nologo','-nostdlib','-langversion:latest','-target:winexe','-optimize+',
               '-platform:anycpu','-nowarn:CS0168,CS0219,CS1998,CS4014', "-out:`"$out`"") + $extra + $refs + $src
    Set-Content -Path $rsp -Value $lines -Encoding UTF8
    & dotnet $csc.FullName -noconfig "@$rsp"
    if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız: $out" }
}

# 1) Önizleme sürümü (yönetici gerektirmez): simge, rehber, betikler ve ekran görüntüleri
Write-Host '>> Önizleme derleniyor...'
$prevExe = Join-Path $build 'DPIKalkan.preview.exe'
Invoke-Csc $prevExe @('-define:PREVIEW')
if ($Check) { Write-Host '>> Derleme hatası yok.'; return }

$ico = Join-Path $build 'app.ico'
Start-Process $prevExe -ArgumentList '--make-icon', "`"$ico`"" -Wait -NoNewWindow
if (-not (Test-Path $ico)) { throw 'Simge üretilemedi.' }

if ($Preview) {
    Write-Host '>> Ekran görüntüleri üretiliyor...'
    $shots = Join-Path $build 'screens'
    Remove-Item -Recurse -Force $shots -ErrorAction SilentlyContinue
    Start-Process $prevExe -ArgumentList '--screenshot', "`"$shots`"" -Wait
}

# 2) Asıl sürüm
Write-Host '>> Uygulama derleniyor...'
if (Test-Path $appDir) { Remove-Item -Recurse -Force $appDir }
New-Item -ItemType Directory -Force $appDir | Out-Null
Invoke-Csc (Join-Path $appDir 'DPIKalkan.exe') @("-win32manifest:`"$root\app.manifest`"", "-win32icon:`"$ico`"")

# 3) GoodbyeDPI dosyaları, lisanslar, rehber, yedek betikler
foreach ($arch in 'x86','x86_64') {
    $dst = Join-Path $appDir "bin\$arch"
    New-Item -ItemType Directory -Force $dst | Out-Null
    Copy-Item (Join-Path $tp "$arch\*") $dst
}
$lic = Join-Path $appDir 'lisanslar'
New-Item -ItemType Directory -Force $lic | Out-Null
Copy-Item (Join-Path $tp 'licenses\*') $lic
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $lic 'LICENSE-dpikalkan.txt')
Copy-Item (Join-Path $root 'THIRD_PARTY_NOTICES.md') (Join-Path $lic 'THIRD_PARTY_NOTICES.md')
Copy-Item (Join-Path $root 'OKUBENI.txt') $appDir
Start-Process $prevExe -ArgumentList '--export-guide', "`"$root\docs\Rehber.html`"", "`"$appDir\Rehber.html`"" -Wait -NoNewWindow
Start-Process $prevExe -ArgumentList '--export-scripts', "`"$appDir\betikler`"" -Wait -NoNewWindow
if (-not (Test-Path "$appDir\Rehber.html")) { throw 'Rehber üretilemedi.' }
if (-not (Test-Path "$appDir\betikler\01_turkiye_onerilen.cmd")) { throw 'Betikler üretilemedi.' }

# 4) Zip paketi
$zip = Join-Path $dist "DPI-Kalkan-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $appDir -DestinationPath $zip -CompressionLevel Optimal
Write-Host ">> Tamam: $zip"
