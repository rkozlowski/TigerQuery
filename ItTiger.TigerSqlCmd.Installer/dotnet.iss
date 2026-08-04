[Code]
// dotnet.iss
// .NET runtime prerequisite detection and installation helpers.
//
// Requires the following defines, generated into WorkingDir\DotNetRuntime.iss by
// BuildInstaller.ps1 from the published tiger-sqlcmd.runtimeconfig.json:
//   DotNetFrameworkName      e.g. "Microsoft.NETCore.App"
//   DotNetRuntimeVersion     e.g. "10.0.0"
//   DotNetRuntimeMajorMinor  e.g. "10.0"
{ Removes and returns the leading dot-separated token of S as a number (-1 if not numeric). }
function TakeVersionToken(var S: string): Integer;
var
    P: Integer;
    Part: string;
begin
    P := Pos('.', S);
    if P > 0 then
    begin
        Part := Copy(S, 1, P - 1);
        Delete(S, 1, P);
    end
    else
    begin
        Part := S;
        S := '';
    end;
    Result := StrToIntDef(Part, -1);
end;


procedure ParseRuntimeVersion(Version: string; var Major, Minor, Patch: Integer);
var
    P: Integer;
begin
    { Ignore prerelease suffixes like "-rc.1.25451.107" }
    P := Pos('-', Version);
    if P > 0 then
        Version := Copy(Version, 1, P - 1);
    Major := TakeVersionToken(Version);
    Minor := TakeVersionToken(Version);
    Patch := TakeVersionToken(Version);
end;


{ True when Installed satisfies Required under the default .NET host roll-forward
  policy: same major version, and minor.patch greater than or equal. }
function IsRuntimeVersionCompatible(Installed, Required: string): Boolean;
var
    IMaj, IMin, IPat, RMaj, RMin, RPat: Integer;
begin
    ParseRuntimeVersion(Installed, IMaj, IMin, IPat);
    ParseRuntimeVersion(Required, RMaj, RMin, RPat);
    Result :=
        (IMaj >= 0) and (IMaj = RMaj) and
        ((IMin > RMin) or ((IMin = RMin) and (IPat >= RPat)));
end;


{ Resolves the 64-bit .NET host via the official install-location registry value
  (always in the 32-bit registry view), falling back to the default location.
  Returns '' when no host is present. }
function GetDotNetHostPath(): string;
var
    HostPath: string;
begin
    Result := '';
    if not IsWin64 then
        exit;
    if RegQueryStringValue(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64',
        'InstallLocation', HostPath) then
    begin
        HostPath := AddBackslash(HostPath) + 'dotnet.exe';
        if FileExists(HostPath) then
        begin
            Result := HostPath;
            exit;
        end;
    end;
    HostPath := ExpandConstant('{commonpf64}\dotnet\dotnet.exe');
    if FileExists(HostPath) then
        Result := HostPath;
end;


{ Runs "dotnet --list-runtimes" and returns True when a compatible
  {#DotNetFrameworkName} {#DotNetRuntimeVersion} runtime is installed. }
function IsRequiredDotNetInstalled(): Boolean;
var
    HostPath, OutputFile, Line, Prefix: string;
    Lines: TArrayOfString;
    ResultCode, I, P: Integer;
begin
    Result := False;
    HostPath := GetDotNetHostPath();
    if HostPath = '' then
    begin
        Log('.NET host (dotnet.exe) not found; the required runtime is not installed.');
        exit;
    end;
    OutputFile := ExpandConstant('{tmp}\dotnet-list-runtimes.txt');
    if not Exec(ExpandConstant('{cmd}'),
        '/C ""' + HostPath + '" --list-runtimes > "' + OutputFile + '" 2>&1"',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    begin
        Log(Format('"%s" --list-runtimes failed (exit code %d).', [HostPath, ResultCode]));
        exit;
    end;
    if not LoadStringsFromFile(OutputFile, Lines) then
        exit;
    Prefix := '{#DotNetFrameworkName} ';
    for I := 0 to GetArrayLength(Lines) - 1 do
    begin
        Line := Trim(Lines[I]);
        if Pos(Prefix, Line) = 1 then
        begin
            Line := Copy(Line, Length(Prefix) + 1, Length(Line));
            P := Pos(' ', Line);
            if P > 0 then
                Line := Copy(Line, 1, P - 1);
            if IsRuntimeVersionCompatible(Line, '{#DotNetRuntimeVersion}') then
            begin
                Log(Format('Found compatible .NET runtime: {#DotNetFrameworkName} %s.', [Line]));
                Result := True;
                exit;
            end;
        end;
    end;
    Log('No compatible {#DotNetFrameworkName} {#DotNetRuntimeVersion} (or later servicing/minor) runtime found.');
end;


{ Downloads the latest {#DotNetRuntimeMajorMinor} runtime installer from the
  official Microsoft aka.ms channel URL into the setup temp directory and runs
  it. Returns '' on success (runtime verified installed afterwards), otherwise
  a user-facing error message. }
function InstallRequiredDotNet(): string;
var
    SetupPath, Args: string;
    ResultCode: Integer;
begin
    Result := '';
    Log('Downloading the Microsoft .NET {#DotNetRuntimeMajorMinor} Runtime (x64) installer...');
    try
        DownloadTemporaryFile(
            'https://aka.ms/dotnet/{#DotNetRuntimeMajorMinor}/dotnet-runtime-win-x64.exe',
            'dotnet-runtime-win-x64.exe', '', nil);
    except
        Result := 'The Microsoft .NET Runtime installer could not be downloaded: '
            + GetExceptionMessage + #13#10#13#10
            + 'Please install the Microsoft .NET {#DotNetRuntimeMajorMinor} Runtime (x64) manually from'#13#10
            + 'https://dotnet.microsoft.com/download/dotnet/{#DotNetRuntimeMajorMinor}'#13#10
            + 'and then run TigerSqlCmd Setup again.';
        exit;
    end;
    SetupPath := ExpandConstant('{tmp}\dotnet-runtime-win-x64.exe');
    if WizardSilent() then
        Args := '/install /quiet /norestart'
    else
        Args := '/install /passive /norestart';
    Log('Running the Microsoft .NET Runtime installer: ' + SetupPath + ' ' + Args);
    if not Exec(SetupPath, Args, '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
    begin
        Result := 'The Microsoft .NET Runtime installer could not be started.'#13#10#13#10
            + 'Please install the Microsoft .NET {#DotNetRuntimeMajorMinor} Runtime (x64) manually from'#13#10
            + 'https://dotnet.microsoft.com/download/dotnet/{#DotNetRuntimeMajorMinor}'#13#10
            + 'and then run TigerSqlCmd Setup again.';
        exit;
    end;
    { 3010 = success, restart required; 1641 = success, restart initiated }
    if (ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1641) then
    begin
        if ResultCode = 1602 then
            Result := 'The Microsoft .NET Runtime installation was cancelled.'#13#10#13#10
                + 'TigerSqlCmd cannot be installed without the Microsoft .NET '
                + '{#DotNetRuntimeMajorMinor} Runtime (x64).'
        else
            Result := Format('The Microsoft .NET Runtime installation failed (exit code %d).', [ResultCode])
                + #13#10#13#10
                + 'Please install the Microsoft .NET {#DotNetRuntimeMajorMinor} Runtime (x64) manually from'#13#10
                + 'https://dotnet.microsoft.com/download/dotnet/{#DotNetRuntimeMajorMinor}'#13#10
                + 'and then run TigerSqlCmd Setup again.';
        exit;
    end;
    if not IsRequiredDotNetInstalled() then
        Result := 'The Microsoft .NET Runtime installer completed, but the required '
            + '{#DotNetFrameworkName} {#DotNetRuntimeVersion} runtime was still not detected.'#13#10#13#10
            + 'Please install the Microsoft .NET {#DotNetRuntimeMajorMinor} Runtime (x64) manually from'#13#10
            + 'https://dotnet.microsoft.com/download/dotnet/{#DotNetRuntimeMajorMinor}'#13#10
            + 'and then run TigerSqlCmd Setup again.';
end;
