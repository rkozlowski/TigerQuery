; ============================================
; TigerSqlCmd Installer Script
; InstallType: Per-user
; Author:      IT Tiger
; ============================================

#ifnexist "WorkingDir\BuildDefines.iss"
  #error "WorkingDir\BuildDefines.iss is missing - run BuildInstaller.ps1"
#endif
#include "WorkingDir\BuildDefines.iss"
#include "environment.iss"
#include "dotnet.iss"

[Setup]
; Stable identity: changing AppId would create a side-by-side product instead of an upgrade.
AppId=ItTiger.TigerSqlCmd
AppName=TigerSqlCmd
AppVersion={#ProductVersion}
AppVerName=TigerSqlCmd {#ProductVersion}
DefaultDirName={localappdata}\Programs\IT Tiger\TigerSqlCmd
DefaultGroupName=TigerSqlCmd
OutputDir=Output
OutputBaseFilename={#SetupBaseFilename}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
VersionInfoCompany=IT Tiger
VersionInfoDescription=TigerSqlCmd installer
VersionInfoProductName=TigerSqlCmd
VersionInfoProductVersion={#ProductVersionInfo}
VersionInfoVersion={#ProductVersionInfo}
UninstallDisplayIcon={app}\tiger-sqlcmd.exe
UninstallDisplayName=TigerSqlCmd
AlwaysShowDirOnReadyPage=yes
ChangesEnvironment=yes
AppPublisher=IT Tiger
AppPublisherURL=https://www.ittiger.net/
AppSupportURL=https://www.ittiger.net/projects/tigerquery/
AppUpdatesURL=https://github.com/rkozlowski/TigerQuery/releases

[Files]
Source: "WorkingDir\cli\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "WorkingDir\VERSION.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\TigerSqlCmd"; Filename: "{cmd}"; Parameters: "/K &quot;&quot;{app}\tiger-sqlcmd.exe&quot;&quot; --help"; WorkingDir: "{app}"

[Code]
var
  DotNetInstallRequested: Boolean;

function CmdLineParamExists(const Value: string): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Value) = 0 then
    begin
      Result := True;
      exit;
    end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  DotNetInstallRequested := False;
  if IsRequiredDotNetInstalled() then
    exit;

  if WizardSilent() then
  begin
    if CmdLineParamExists('/INSTALLDOTNET') then
    begin
      DotNetInstallRequested := True;
      exit;
    end;
    Log('Prerequisite missing: Microsoft .NET {#DotNetRuntimeMajorMinor} Runtime (x64).');
    SuppressibleMsgBox(
      'TigerSqlCmd requires the Microsoft .NET {#DotNetRuntimeMajorMinor} Runtime (x64).'#13#10#13#10
      + 'Install it first or rerun Setup with /INSTALLDOTNET.',
      mbCriticalError, MB_OK, IDOK);
    Result := False;
    exit;
  end;

  case TaskDialogMsgBox('.NET Runtime required',
         'TigerSqlCmd requires the Microsoft .NET {#DotNetRuntimeMajorMinor} Runtime (x64).'#13#10#13#10
         + 'Download and install it now from Microsoft?',
         mbConfirmation, MB_YESNO, ['&Download and install', '&Cancel setup'], 0) of
    IDYES: DotNetInstallRequested := True;
  else
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if DotNetInstallRequested then
    Result := InstallRequiredDotNet();
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    EnvAddPath(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    EnvRemovePath(ExpandConstant('{app}'));
end;
