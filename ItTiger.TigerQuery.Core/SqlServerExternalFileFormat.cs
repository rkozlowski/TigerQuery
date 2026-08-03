namespace ItTiger.TigerQuery.Core;

/// <summary>Identifies how an external-value file is interpreted.</summary>
public enum SqlServerExternalFileFormat
{
    /// <summary>The entire UTF-8 text file is the value, with no trimming.</summary>
    Text = 0,

    /// <summary>A named top-level JSON string property is the value.</summary>
    Json = 1
}
