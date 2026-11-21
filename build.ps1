# DPI Kalkan derleme betiği
#   powershell -ExecutionPolicy Bypass -File build.ps1                      -> derler; taşınabilir zip + kurulum paketi + SHA256SUMS (dist\)
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Preview             -> ek olarak ekran görüntüleri üretir (build\screens)
#   powershell -ExecutionPolicy Bypass -File build.ps1 -Check               -> yalnızca derleme hatalarını denetler
#   powershell -ExecutionPolicy Bypass -File build.ps1 -VerifyReproducible  -> iki kez derleyip exe/zip/kurulum özetlerini karşılaştırır
#   -NoSetup : Inno Setup kurulum paketini üretme
#
# Tekrarlanabilir derleme: csc -deterministic -pathmap, sıralı kaynak listesi, sabit dosya sırası ve sabit zaman damgası
# (SOURCE_DATE_EPOCH ortam değişkeni, yoksa son commit zamanı). Aynı koddan iki derleme aynı SHA-256'yı verir.
#
# İmzalama kancası (şimdilik pasif): DPK_SIGN_ARGS ortam değişkeni tanımlıysa DPIKalkan.exe ve kurulum paketi
# signtool ile imzalanır. Örnek (kod imzalama sertifikası alındığında):
#   $env:DPK_SIGNTOOL  = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe'
#   $env:DPK_SIGN_ARGS = '/sha1 <sertifika parmak izi> /fd sha256 /tr http://time.certum.pl /td sha256'
param([switch]$Preview, [switch]$Check, [switch]$NoSetup, [switch]$VerifyReproducible)
$ErrorActionPreference = 'Stop'
$root   = $PSScriptRoot
# GoodbyeDPI 0.2.3rc3 resmi dosyaları (değiştirilmemiş; bkz. third_party\goodbyedpi\README.md)
$tp     = Join-Path $root 'third_party\goodbyedpi'
$build  = Join-Path $root 'build'
$dist   = Join-Path $root 'dist'
$appDir = Join-Path $build 'paket\DPI Kalkan'
$utf8   = New-Object Text.UTF8Encoding $false

# ---------------------------------------------------------------- sürüm: tek kaynak src\Program.cs
$prog = Get-Content (Join-Path $root 'src\Program.cs') -Raw -Encoding UTF8
function Get-Attr([string]$name) { [regex]::Match($prog, '\[assembly: ' + $name + '\("([^"]+)"\)\]').Groups[1].Value }
$asmVer  = Get-Attr 'AssemblyVersion'
$fileVer = Get-Attr 'AssemblyFileVersion'
$infoVer = Get-Attr 'AssemblyInformationalVersion'
if ($asmVer -notmatch '^\d+\.\d+\.\d+\.0$') { throw "src\Program.cs içindeki AssemblyVersion geçersiz: '$asmVer' (biçim: 2.4.0.0)" }
$version = $asmVer.Substring(0, $asmVer.Length - 2)
if ($fileVer -ne $asmVer -or $infoVer -ne $version) {
    throw "Sürüm numaraları uyuşmuyor: AssemblyVersion=$asmVer, AssemblyFileVersion=$fileVer, AssemblyInformationalVersion=$infoVer"
}

# ---------------------------------------------------------------- sabit zaman damgası
$epoch = $env:SOURCE_DATE_EPOCH
if (-not $epoch) { try { $epoch = (& git -C $root log -1 --format=%ct 2>$null) | Select-Object -First 1 } catch { $epoch = $null } }
if (-not $epoch) { $epoch = '1767225600' }   # 2026-01-01 00:00:00 UTC
$stamp = [DateTimeOffset]::FromUnixTimeSeconds([int64]$epoch)

# ---------------------------------------------------------------- derleyici: global.json'daki .NET SDK sürümü
$sdkWant = $null
$gj = Join-Path $root 'global.json'
if (Test-Path $gj) { $sdkWant = (Get-Content $gj -Raw | ConvertFrom-Json).sdk.version }
$sdks = @(& dotnet --list-sdks 2>$null) | ForEach-Object {
    if ($_ -match '^(\S+)\s+\[(.+)\]\s*$') { [pscustomobject]@{ Version = $Matches[1]; Dir = (Join-Path $Matches[2] $Matches[1]) } }
} | Where-Object { Test-Path (Join-Path $_.Dir 'Roslyn\bincore\csc.dll') }
if (-not $sdks) { throw '.NET SDK bulunamadı (csc.dll). https://dot.net adresinden .NET SDK kurun.' }
$sdk = $sdks | Where-Object { $_.Version -eq $sdkWant } | Select-Object -First 1
if (-not $sdk) {
    $sdk = $sdks | Sort-Object { [version]($_.Version -replace '-.*$', '') } | Select-Object -Last 1
    Write-Warning ".NET SDK $sdkWant bulunamadı; $($sdk.Version) kullanılıyor (çıktı CI'dakinden farklı olabilir)."
}
$csc = Join-Path $sdk.Dir 'Roslyn\bincore\csc.dll'
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path $fw)) { $fw = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319" }

New-Item -ItemType Directory -Force $build, $dist | Out-Null
$refs = 'mscorlib','System','System.Core','System.Drawing','System.Windows.Forms','System.Xml',
        'System.ServiceProcess','System.Management','System.Security',
        'System.Web.Extensions','System.IO.Compression','System.IO.Compression.FileSystem' | ForEach-Object { "-r:`"$fw\$_.dll`"" }
# kaynak dosyalar sabit (ordinal) sırada
[string[]]$srcFiles = Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
[Array]::Sort($srcFiles, [StringComparer]::Ordinal)
$src = $srcFiles | ForEach-Object { "`"$_`"" }

function Invoke-Csc([string]$out, [string[]]$extra) {
    $rsp = Join-Path $build 'csc.rsp'
    $lines = @('-nologo','-nostdlib','-langversion:latest','-target:winexe','-optimize+','-debug-','-deterministic',
               "`"-pathmap:$root=/_/`"", '-platform:anycpu','-nowarn:CS0168,CS0219,CS1998,CS4014', "-out:`"$out`"") + $extra + $refs + $src
    [IO.File]::WriteAllLines($rsp, $lines, $utf8)
    & dotnet $csc -noconfig "@$rsp"
    if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız: $out" }
}

# ---------------------------------------------------------------- imzalama kancası (pasif)
$signArgs = $env:DPK_SIGN_ARGS
$signTool = $null
if ($signArgs) {
    $signTool = if ($env:DPK_SIGNTOOL) { $env:DPK_SIGNTOOL } else { (Get-Command signtool.exe -ErrorAction SilentlyContinue).Source }
    if (-not $signTool -or -not (Test-Path $signTool)) { throw 'DPK_SIGN_ARGS tanımlı ama signtool.exe bulunamadı (DPK_SIGNTOOL ile yolunu verin).' }
}
function Invoke-Sign([string]$file) {
    if (-not $signArgs) { return }
    Write-Host ">> İmzalanıyor: $file"
    $p = Start-Process $signTool -ArgumentList "sign $signArgs `"$file`"" -Wait -NoNewWindow -PassThru
    if ($p.ExitCode -ne 0) { throw "İmzalanamadı: $file" }
    $p = Start-Process $signTool -ArgumentList "verify /pa `"$file`"" -Wait -NoNewWindow -PassThru
    if ($p.ExitCode -ne 0) { throw "İmza doğrulanamadı: $file" }
}

function Get-Sha256([string]$file) { (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() }

# ---------------------------------------------------------------- 1) önizleme sürümü: simge, rehber, betikler, ekran görüntüleri
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

# ---------------------------------------------------------------- 2) asıl sürüm (manifest sürümü exe sürümüne eşitlenir)
Write-Host ">> Uygulama derleniyor ($version)..."
if (Test-Path $appDir) { Remove-Item -Recurse -Force $appDir }
New-Item -ItemType Directory -Force $appDir | Out-Null
$manifest = Join-Path $build 'app.manifest'
$mf = [IO.File]::ReadAllText((Join-Path $root 'app.manifest'))
$mf = [regex]::Replace($mf, '(<assemblyIdentity\s+version=")[^"]*(")', ('${1}' + $asmVer + '${2}'))
[IO.File]::WriteAllText($manifest, $mf, $utf8)
$exePath = Join-Path $appDir 'DPIKalkan.exe'
Invoke-Csc $exePath @("-win32manifest:`"$manifest`"", "-win32icon:`"$ico`"")
Invoke-Sign $exePath

# ---------------------------------------------------------------- 3) GoodbyeDPI dosyaları, lisanslar, rehber, yedek betikler
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
# tüm dosyalara sabit zaman damgası (zip ve kurulum paketi için)
Get-ChildItem $appDir -Recurse -File | ForEach-Object { $_.CreationTimeUtc = $stamp.UtcDateTime; $_.LastWriteTimeUtc = $stamp.UtcDateTime }

# ---------------------------------------------------------------- 4) taşınabilir zip: sabit sıra, sabit zaman, '/' ayırıcı
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zip = Join-Path $dist "DPI-Kalkan-$version.zip"
Remove-Item $zip, "$zip.sha256" -ErrorAction SilentlyContinue
[string[]]$rels = Get-ChildItem $appDir -Recurse -File | ForEach-Object { $_.FullName.Substring($appDir.Length + 1).Replace('\', '/') }
[Array]::Sort($rels, [StringComparer]::Ordinal)
$fs = [IO.File]::Open($zip, [IO.FileMode]::CreateNew)
try {
    $za = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
    foreach ($rel in $rels) {
        $entry = $za.CreateEntry("DPI Kalkan/$rel", [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $stamp
        $in = [IO.File]::OpenRead((Join-Path $appDir $rel))
        $out = $entry.Open()
        try { $in.CopyTo($out) } finally { $out.Dispose(); $in.Dispose() }
    }
    $za.Dispose()
} finally { $fs.Dispose() }
Write-Host ">> Zip: $zip"

# ---------------------------------------------------------------- 5) kurulum paketi (Inno Setup)
$setup = Join-Path $dist "DPI-Kalkan-$version-Setup.exe"
if (-not $NoSetup) {
    $iscc = @($env:ISCC, "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
              "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) {
        if ($env:CI) { throw 'Inno Setup (ISCC.exe) bulunamadı.' }
        Write-Warning 'Inno Setup (ISCC.exe) bulunamadı; kurulum paketi atlandı. https://jrsoftware.org/isdl.php'
        $setup = $null
    } else {
        Write-Host '>> Kurulum paketi derleniyor (Inno Setup)...'
        Remove-Item $setup -ErrorAction SilentlyContinue
        $t = $stamp.UtcDateTime
        $isccArgs = "/Q `"/DAppVersion=$version`" `"/DSourceDir=$appDir`" `"/DOutDir=$dist`" `"/DIconFile=$ico`" " +
                    "`"/DTouchDate=$($t.ToString('yyyy-MM-dd'))`" `"/DTouchTime=$($t.ToString('HH:mm'))`""
        if ($signArgs) { $isccArgs += " /DSIGN `"/Sdpksign=$signTool sign $signArgs `$f`"" }
        $isccArgs += " `"$(Join-Path $root 'installer\DPIKalkan.iss')`""
        $p = Start-Process $iscc -ArgumentList $isccArgs -Wait -NoNewWindow -PassThru
        if ($p.ExitCode -ne 0 -or -not (Test-Path $setup)) { throw 'Kurulum paketi derlenemedi.' }
        Write-Host ">> Kurulum: $setup"
    }
} else { $setup = $null }

# ---------------------------------------------------------------- 6) SHA256SUMS: paketler + kritik iç dosyalar
$sums = Join-Path $dist "DPI-Kalkan-$version.SHA256SUMS.txt"
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("$(Get-Sha256 $zip)  $(Split-Path $zip -Leaf)")
if ($setup) { $lines.Add("$(Get-Sha256 $setup)  $(Split-Path $setup -Leaf)") }
foreach ($rel in 'DPIKalkan.exe', 'bin/x86_64/goodbyedpi.exe', 'bin/x86_64/WinDivert.dll', 'bin/x86_64/WinDivert64.sys',
                 'bin/x86/goodbyedpi.exe', 'bin/x86/WinDivert.dll', 'bin/x86/WinDivert32.sys', 'bin/x86/WinDivert64.sys') {
    $lines.Add("$(Get-Sha256 (Join-Path $appDir $rel))  DPI Kalkan/$rel")
}
[IO.File]::WriteAllText($sums, (($lines -join "`n") + "`n"), $utf8)
Write-Host ">> Tamam ($version). Özetler: $sums"
$lines | ForEach-Object { Write-Host "   $_" }

# ---------------------------------------------------------------- 7) tekrarlanabilirlik denetimi (isteğe bağlı)
if ($VerifyReproducible) {
    if ($signArgs) { Write-Warning 'İmzalama açıkken (imza zaman damgası) tekrarlanabilirlik denetlenmez.'; return }
    $first = [ordered]@{ 'DPIKalkan.exe' = (Get-Sha256 $exePath); (Split-Path $zip -Leaf) = (Get-Sha256 $zip) }
    if ($setup) { $first[(Split-Path $setup -Leaf)] = Get-Sha256 $setup }
    Write-Host '>> Tekrarlanabilirlik: aynı kaynaktan ikinci derleme...'
    $childArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($NoSetup) { $childArgs += '-NoSetup' }
    & powershell @childArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'İkinci derleme başarısız.' }
    $second = [ordered]@{ 'DPIKalkan.exe' = (Get-Sha256 $exePath); (Split-Path $zip -Leaf) = (Get-Sha256 $zip) }
    if ($setup) { $second[(Split-Path $setup -Leaf)] = Get-Sha256 $setup }
    $bad = @()
    foreach ($k in $first.Keys) {
        $same = $first[$k] -eq $second[$k]
        Write-Host ("   {0,-34} {1}  {2}" -f $k, $(if ($same) { 'AYNI  ' } else { 'FARKLI' }), $second[$k])
        if (-not $same) { $bad += $k }
    }
    if ($bad.Count -gt 0) { throw ('Tekrarlanabilir değil: ' + ($bad -join ', ')) }
    Write-Host '>> Tekrarlanabilir: iki derlemenin özetleri aynı.'
}
