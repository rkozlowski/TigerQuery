; User PATH support, based on TigerWrap's environment.iss implementation.
[Code]
function IsPathInList(Path: string; Paths: string): Boolean;
var
  Item, Tail: string;
  Separator: Integer;
begin
  Result := False;
  Tail := Paths;
  while Length(Tail) > 0 do
  begin
    Separator := Pos(';', Tail);
    if Separator < 1 then
    begin
      Item := Tail;
      Tail := '';
    end
    else
    begin
      Item := Copy(Tail, 1, Separator - 1);
      Tail := Copy(Tail, Separator + 1, Length(Tail) - Separator);
    end;
    if SameText(Item, Path) then
    begin
      Result := True;
      exit;
    end;
  end;
end;

function WithoutPath(Paths: string; Path: string): string;
var
  Item, Tail: string;
  Separator: Integer;
begin
  Result := '';
  Tail := Paths;
  while Length(Tail) > 0 do
  begin
    Separator := Pos(';', Tail);
    if Separator < 1 then
    begin
      Item := Tail;
      Tail := '';
    end
    else
    begin
      Item := Copy(Tail, 1, Separator - 1);
      Tail := Copy(Tail, Separator + 1, Length(Tail) - Separator);
    end;
    if (Item <> '') and not SameText(Item, Path) then
    begin
      if Result <> '' then
        Result := Result + ';';
      Result := Result + Item;
    end;
  end;
end;

procedure EnvAddPath(Path: string);
var
  Paths: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Paths) then
    Paths := '';
  if IsPathInList(Path, Paths) then
    exit;
  if Paths = '' then
    Paths := Path
  else if Copy(Paths, Length(Paths), 1) = ';' then
    Paths := Paths + Path
  else
    Paths := Paths + ';' + Path;
  if not RegWriteStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Paths) then
    RaiseException('Could not add TigerSqlCmd to the user PATH.');
end;

procedure EnvRemovePath(Path: string);
var
  Paths: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Paths) then
    exit;
  if not IsPathInList(Path, Paths) then
    exit;
  Paths := WithoutPath(Paths, Path);
  if not RegWriteStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', Paths) then
    RaiseException('Could not remove TigerSqlCmd from the user PATH.');
end;
