using ItTiger.Core;

namespace ItTiger.TigerQuery.CliCore;

/// <summary>
/// Exit codes produced by the reusable SQL Server connection commands.
/// </summary>
/// <remarks>
/// Ideally a reusable command library would report semantic outcomes
/// (<c>TigerCliExitCategory</c>/<c>TigerCliExitKind</c>) and let each host application map
/// them onto its own exit-code enum. TigerCli's typed command handlers currently bind the
/// enum at the handler, so this library has to own these values for now; the host-owned
/// mapping model is a deferred TigerCli design issue, not the preferred long-term shape.
/// </remarks>
[TigerText("SQL Server connection command exit codes")]
public enum SqlServerConnectionExitCode
{
    [TigerText("OK", Description = "The operation completed successfully.")]
    Ok = 0,

    [TigerText("Invalid arguments", Description = "The supplied connection options failed validation.")]
    InvalidArguments = 2,

    [TigerText("Not found", Description = "The named SQL Server connection does not exist.")]
    NotFound = 4,

    [TigerText("Already exists", Description = "A SQL Server connection with that name already exists.")]
    AlreadyExists = 5,
}
