; DPI Kalkan kurulum paketi (Inno Setup 6.7+). Doğrudan değil build.ps1 ile derlenir:
;   ISCC /DAppVersion=2.4.0 /DSourceDir=...\build\paket\DPI Kalkan /DOutDir=...\dist /DIconFile=...\build\app.ico
;        /DTouchDate=YYYY-MM-DD /DTouchTime=HH:MM [/DSIGN /Sdpksign=...] installer\DPIKalkan.iss
;
; - Yalnızca 64-bit Windows 10/11, yönetici izniyle, Program Files\DPI Kalkan klasörüne kurar (korumalı kurulum:
;   arka plan hizmeti ve "Windows açılışında başlat" yalnızca burada kullanılabilir).
; - Yükseltmede veri\ klasörü (ayarlar, profiller, günlük) korunur; dosyalar değiştirilmeden önce eski sürümün
;   --prepare-upgrade komutu bu klasördeki uygulamayı, GoodbyeDPI'ı ve hizmeti durdurur.
; - Kaldırırken önce "DPIKalkan.exe --uninstall --quiet" çalışır: hizmet, açılış görevi, WinDivert kaydı,
;   Windows proxy ve DNS değişiklikleri geri alınır.
; - Uygulamanın otomatik güncellemesi bu paketi /VERYSILENT /DKUPDATED=<eski sürüm> /DKRESUME=0|1 ile çalıştırır.

#ifndef AppVersion
  #error AppVersion tanımlı değil; kurulum paketini build.ps1 ile derleyin.
#endif
#ifndef SourceDir
  #error SourceDir tanımlı değil; kurulum paketini build.ps1 ile derleyin.
#endif
#ifndef OutDir
  #define OutDir "..\dist"
#endif
#ifndef TouchDate
  #define TouchDate "current"
#endif
#ifndef TouchTime
  #define TouchTime "current"
#endif

#define AppName "DPI Kalkan"
#define AppExe "DPIKalkan.exe"
#define AppPublisher "Osman Barış MEDİNE (Mr Leywin)"
#define AppUrl "https://github.com/Mr-Leywin/dpi-kalkan"
#define AppCopyright "Copyright (c) 2026 Osman Barış MEDİNE (Mr Leywin)"

[Setup]
; AppId uygulamadaki Updater.InnoAppId ile aynı olmalı (kaldırma kaydından kurulum klasörü okunur)
AppId={{DD5D79D5-C88D-4B57-A5C9-1C586148DC98}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright={#AppCopyright}
VersionInfoVersion={#AppVersion}.0
VersionInfoProductVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Kurulumu
VersionInfoCopyright={#AppCopyright}
DefaultDirName={autopf}\{#AppName}
DisableDirPage=yes
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutDir}
OutputBaseFilename=DPI-Kalkan-{#AppVersion}-Setup
#ifdef IconFile
SetupIconFile={#IconFile}
#endif
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; uygulamayı Restart Manager yerine kendi --prepare-upgrade komutumuz kapatır
CloseApplications=no
RestartApplications=no
SetupMutex=DPIKalkanKurulum
SetupLogging=yes
ShowLanguageDialog=no
; tekrarlanabilirlik: kurulan dosyaların tarihi sabit (build.ps1 kaynak zamanını verir)
TimeStampsInUTC=yes
TouchDate={#TouchDate}
TouchTime={#TouchTime}
#ifdef SIGN
; imzalama kancası: build.ps1, DPK_SIGN_ARGS tanımlıysa /DSIGN ve /Sdpksign=... ile çağırır
SignTool=dpksign
SignedUninstaller=yes
#endif

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstüne kısayol oluştur"; GroupDescription: "Ek kısayollar:"; Flags: unchecked

[Files]
; veri\ klasörü pakette yoktur; uygulama ilk açılışta oluşturur ve yükseltmede dokunulmaz
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs touch

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "GoodbyeDPI kontrol paneli"
Name: "{autoprograms}\{#AppName} Kullanım Rehberi"; Filename: "{app}\Rehber.html"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

; DPIKalkan.exe manifesti requireAdministrator: yetkisiz başlatılırsa CreateProcess 740 ("yükseltme gerekiyor") hatası verir.
; postinstall girdileri Inno Setup'ta varsayılan olarak ilk kullanıcının yetkisiz kimliğiyle (runasoriginaluser) çalışır;
; kurulum zaten yönetici olduğundan tüm girdiler runascurrentuser ile yönetici olarak başlatılır (ikinci UAC sorusu çıkmaz).
; [Code] içindeki Exec çağrıları da kurulumun yönetici kimliğiyle çalışır (ExecAsOriginalUser kullanılmaz).
[Run]
Filename: "{app}\{#AppExe}"; Description: "DPI Kalkan'ı şimdi başlat"; Flags: nowait postinstall skipifsilent runascurrentuser
; otomatik güncellemeden sonra uygulamayı "güncellendi" bilgisiyle yeniden aç
Filename: "{app}\{#AppExe}"; Parameters: "--tray --updated {param:DKUPDATED|} {code:ResumeArg}"; Flags: nowait runascurrentuser; Check: IsSilentUpdate

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall --quiet"; Flags: runhidden waituntilterminated runascurrentuser; RunOnceId: "DPIKalkanTemizKaldir"

[Code]
var
  ServiceWasRunning: Boolean;
  InstallDone: Boolean;

function IsSilentUpdate: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:DKUPDATED|}') <> '');
end;

function ResumeArg(Param: String): String;
begin
  if ExpandConstant('{param:DKRESUME|0}') = '1' then
    Result := '--resume'
  else
    Result := '';
end;

function IsDotNet48Installed: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not IsDotNet48Installed then
  begin
    SuppressibleMsgBox('DPI Kalkan için .NET Framework 4.8 gerekir.' + #13#10#13#10 +
      'Windows Update ile bilgisayarınızı güncelleyin ya da .NET Framework 4.8''i Microsoft''un sitesinden kurun, sonra kurulumu yeniden çalıştırın.',
      mbCriticalError, MB_OK, IDOK);
    Result := False;
  end;
end;

{ Kurulu sürüm 2.4.0 ya da sonrası mı? (--prepare-upgrade komutunu tanıyor mu) }
function SupportsPrepareUpgrade(Exe: String): Boolean;
var
  Major, Minor, Rev, Build: Word;
begin
  Result := GetVersionComponents(Exe, Major, Minor, Rev, Build) and ((Major > 2) or ((Major = 2) and (Minor >= 4)));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  RC: Integer;
  Exe: String;
begin
  Result := '';
  ServiceWasRunning := False;
  Exe := ExpandConstant('{app}\{#AppExe}');
  if not FileExists(Exe) then
    Exit;
  if SupportsPrepareUpgrade(Exe) then
  begin
    { bu klasördeki uygulamayı, GoodbyeDPI'ı ve hizmeti durdurur; ayarlara ve sistem değişikliklerine dokunmaz }
    if Exec(Exe, '--prepare-upgrade', '', SW_HIDE, ewWaitUntilTerminated, RC) then
    begin
      ServiceWasRunning := (RC = 10);
      Log('--prepare-upgrade çıkış kodu: ' + IntToStr(RC));
    end
    else
      Log('--prepare-upgrade çalıştırılamadı: ' + SysErrorMessage(RC));
  end
  else
  begin
    { kurulum paketinden önceki (elle kopyalanmış) bir sürüm: hizmeti ve uygulamayı Windows araçlarıyla durdur }
    Exec(ExpandConstant('{sys}\net.exe'), 'stop DPIKalkan', '', SW_HIDE, ewWaitUntilTerminated, RC);
    ServiceWasRunning := (RC = 0);
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM DPIKalkan.exe', '', SW_HIDE, ewWaitUntilTerminated, RC);
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM goodbyedpi.exe', '', SW_HIDE, ewWaitUntilTerminated, RC);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  RC: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    InstallDone := True;
    { hizmet yükseltmeden önce çalışıyorduysa koruma hemen geri gelsin (uygulama açılmasa bile) }
    if ServiceWasRunning then
      Exec(ExpandConstant('{sys}\sc.exe'), 'start DPIKalkan', '', SW_HIDE, ewWaitUntilTerminated, RC);
  end;
end;

procedure DeinitializeSetup;
var
  RC: Integer;
  Exe: String;
begin
  { sessiz güncelleme başarısız olduysa Inno Setup dosyaları geri almıştır: eski sürümü ve korumayı yeniden aç }
  if IsSilentUpdate and (not InstallDone) then
  begin
    try
      if ServiceWasRunning then
        Exec(ExpandConstant('{sys}\sc.exe'), 'start DPIKalkan', '', SW_HIDE, ewNoWait, RC);
      Exe := ExpandConstant('{app}\{#AppExe}');
      if FileExists(Exe) then
        Exec(Exe, '--tray --update-failed ' + ResumeArg(''), '', SW_SHOWNORMAL, ewNoWait, RC);
    except
      Log('Başarısız güncellemeden sonra eski sürüm açılamadı.');
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Data := ExpandConstant('{app}\veri');
    if DirExists(Data) and (not UninstallSilent) then
      if MsgBox('Ayarlar, özel profiller ve günlükler (veri klasörü) de silinsin mi?' + #13#10#13#10 +
                'Hayır derseniz DPI Kalkan''ı yeniden kurduğunuzda ayarlarınız geri gelir.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(Data, True, True, True);
    RemoveDir(ExpandConstant('{app}'));
  end;
end;
