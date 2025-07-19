namespace ItTiger.TigerQuery;
public enum SqlCmdMode
{
    Normal = 0,     // Plain SQL, no sqlcmd commands or variable substitution
    SqlCmd = 1,     // Standard sqlcmd behavior
    SqlCmdEx = 2    // Extended: programmatic variables take precedence over :setvar
}

