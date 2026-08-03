[Code]
// .NET runtime prerequisite support adapted from TigerWrap.
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
  P := Pos('-', Version);
  if P > 0 then
    Version := Copy(Version, 1, P - 1);
  Major := TakeVersionToken(Version);
  Minor := TakeVersionToken(Version);
  Patch := TakeVersionToken(Version);
end;

function IsRuntimeVersionCompatible(Installed, Required: string): Boolean;
var
  IMajor, IMinor, IPatch, RMajor, RMinor, RPatch: Integer;
begin
  ParseRuntimeVersion(Installed, IMajor, IMinor, IPatch);
  ParseRuntimeVersion(Required, RMajor, RMinor, RPatch);
  Result := (IMajor >= 0) and (IMajor = RMajor) and
    ((IMinor > RMinor) or ((IMinor = RMinor) and (IPatch >= RPatch)));
end;

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

function IsRequiredDotNetInstalled(): Boolean;
var
  HostPath, OutputFile, Line, Prefix: string;
  Lines: TArrayOfString;
  ResultCode, I, P: Integer;
begin
  Result := False;
  HostPath := GetDotNetHostPath();
  if HostPath = '' then
    exit;
  OutputFile := ExpandConstant('{tmp}\dotnet-list-runtimes.txt');
  if not Exec(ExpandConstant('{cmd}'),
      '/C ""' + HostPath + '" --list-runtimes > "' + OutputFile + '" 2>&1"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    exit;
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
        Result := True;
        exit;
      end;
    end;
  end;
end;

function InstallRequiredDotNet(): string;
var
  SetupPath, Args: string;
  ResultCode: Integer;
begin
  Result := '';
  try
    DownloadTemporaryFile(
      'https://aka.ms/dotnet/{#DotNetRuntimeMajorMinor}/dotnet-runtime-win-x64.exe',
      'dotnet-runtime-win-x64.exe', '', nil);
  except
    Result := 'The Microsoft .NET Runtime installer could not be downloaded: '
      + GetExceptionMessage + #13#10#13#10
      + 'Install Microsoft .NET {#DotNetRuntimeMajorMinor} Runtime (x64) and rerun Setup.';
    exit;
  end;
  SetupPath := ExpandConstant('{tmp}\dotnet-runtime-win-x64.exe');
  if WizardSilent() then
    Args := '/install /quiet /norestart'
  else
    Args := '/install /passive /norestart';
  if not Exec(SetupPath, Args, '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := 'The Microsoft .NET Runtime installer could not be started.';
    exit;
  end;
  if (ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1641) then
  begin
    Result := Format('The Microsoft .NET Runtime installation failed (exit code %d).', [ResultCode]);
    exit;
  end;
  if not IsRequiredDotNetInstalled() then
    Result := 'The required Microsoft .NET runtime was not detected after installation.';
end;
