using ItTiger.Core;

namespace ItTiger.TigerQuery.Core;

// TigerText carries the display label (source text = member name, so command-line values
// and default labels stay identical) and the description. Both are resolved against the
// consuming app's registered resources for the active culture; the English text here is
// the culture-neutral fallback. Core itself stays resource-free.

/// <summary>Identifies the credential mechanism used by a connection profile.</summary>
public enum AuthenticationType
{
    /// <summary>Uses the operating-system identity through integrated security.</summary>
    [TigerText("Integrated", Description = "Use Windows Integrated Security")]
    Integrated = 0,

    /// <summary>Uses a SQL Server username and password.</summary>
    [TigerText("SqlPassword", Description = "Use SQL Username and Password")]
    SqlPassword = 1,

    //[TigerText("Entra", Description = "Use Entra ID (future)")]
    //Entra
}

/// <summary>Controls SQL Server transport encryption.</summary>
public enum EncryptOption
{
    /// <summary>Allows an unencrypted connection when the server does not support encryption.</summary>
    [TigerText("Optional", Description = "Encrypt only if the server supports it")]
    Optional = 0,

    /// <summary>Requires encryption using the standard SqlClient validation behavior.</summary>
    [TigerText("Mandatory", Description = "Require encryption (SqlClient default)")]
    Mandatory = 1,

    /// <summary>Requires strict TLS and certificate validation.</summary>
    [TigerText("Strict", Description = "Strict TLS with certificate validation")]
    Strict = 2
}

/// <summary>Describes whether a connection targets read-write or read-only workloads.</summary>
public enum ApplicationIntentOption
{
    /// <summary>Targets a read-write replica.</summary>
    [TigerText("ReadWrite", Description = "Read-write workload (default)")]
    ReadWrite = 0,

    /// <summary>Requests routing to a readable secondary when the server supports it.</summary>
    [TigerText("ReadOnly", Description = "Read-only workload (routes to a readable secondary)")]
    ReadOnly = 1
}

/// <summary>Identifies how a persisted password value is protected.</summary>
public enum PasswordEncryptionType
{
    /// <summary>No encrypted password is applicable, such as for integrated authentication.</summary>
    NotApplicable = 0,

    /// <summary>The value is protected with Windows DPAPI for the current user.</summary>
    DPAPI = 1,
    //Vault          // Cloud key vault in future
}
