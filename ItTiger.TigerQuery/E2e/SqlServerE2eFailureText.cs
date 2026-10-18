namespace ItTiger.TigerQuery.E2e;

/// <summary>
/// Appends the underlying failure messages to an E2E lifecycle failure summary.
/// </summary>
/// <remarks>
/// A command-line host prints only the outermost message, so a summary that leaves the
/// cause on its inner exception reports that something failed without saying what. The
/// causes come from SQL Server, the connection store, and the file system; none of them
/// carries a connection string or a password.
/// </remarks>
internal static class SqlServerE2eFailureText
{
    public static string Causes(Exception primaryFailure, Exception? rollbackFailure)
    {
        var text = $" Cause: {primaryFailure.Message}";
        if (rollbackFailure is not null)
            text += $" Rollback failure: {rollbackFailure.Message}";

        return text;
    }
}
