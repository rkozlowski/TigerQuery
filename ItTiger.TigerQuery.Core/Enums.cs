using ItTiger.Core;

namespace ItTiger.TigerQuery.Core;

// TigerText carries the display label (source text = member name, so command-line values
// and default labels stay identical) and the description. Both are resolved against the
// consuming app's registered resources for the active culture; the English text here is
// the culture-neutral fallback. Core itself stays resource-free.

public enum AuthenticationType
{
    [TigerText("Integrated", Description = "Use Windows Integrated Security")]
    Integrated = 0,

    [TigerText("SqlPassword", Description = "Use SQL Username and Password")]
    SqlPassword = 1,

    //[TigerText("Entra", Description = "Use Entra ID (future)")]
    //Entra
}

public enum EncryptOption
{
    [TigerText("Optional", Description = "Encrypt only if the server supports it")]
    Optional = 0,

    [TigerText("Mandatory", Description = "Require encryption (SqlClient default)")]
    Mandatory = 1,

    [TigerText("Strict", Description = "Strict TLS with certificate validation")]
    Strict = 2
}

public enum ApplicationIntentOption
{
    [TigerText("ReadWrite", Description = "Read-write workload (default)")]
    ReadWrite = 0,

    [TigerText("ReadOnly", Description = "Read-only workload (routes to a readable secondary)")]
    ReadOnly = 1
}

public enum PasswordEncryptionType
{
    NotApplicable = 0, // e.g. Integrated auth
    DPAPI = 1,         // Local machine/user
    //Vault          // Cloud key vault in future
}
