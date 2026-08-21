using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.E2e;

/// <summary>
/// Builds the one guarded batch that turns a freshly created, session-owned E2E database
/// into one that can host memory-optimized tables.
/// </summary>
/// <remarks>
/// <para>
/// The capability belongs to the database, not to the bootstrap connection: nothing here
/// reads or writes bootstrap metadata, and the bootstrap is used only as the authorized
/// route to <c>master</c> that created the database moments earlier.
/// </para>
/// <para>
/// Provisioning is generic SQL Server in-memory OLTP support — a
/// <c>MEMORY_OPTIMIZED_DATA</c> filegroup and its data container — and nothing else. No
/// table, schema, index, or other application-specific object is created, so a caller
/// writes their own memory-optimized DDL exactly as they would against any prepared
/// database.
/// </para>
/// </remarks>
internal static class SqlServerE2eMemoryOptimizedProvisioning
{
    /// <summary>The bound name carrying the exact owned database into the batch.</summary>
    public const string DatabaseNameParameter = "@databaseName";

    /// <summary>The bound name carrying the generated filegroup name.</summary>
    public const string FilegroupNameParameter = "@filegroupName";

    /// <summary>The bound name carrying the generated container logical-file name.</summary>
    public const string ContainerNameParameter = "@containerName";

    /// <summary>The bound name carrying the generated container directory name.</summary>
    public const string ContainerDirectoryParameter = "@containerDirectory";

    /// <summary>
    /// Builds the parameter values for one exact owned database.
    /// </summary>
    /// <param name="databaseName">
    /// The exact database this session just created. It is validated against the protected
    /// E2E prefix and character guards while the derived names are generated.
    /// </param>
    public static IReadOnlyDictionary<string, string> BuildParameters(string databaseName) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DatabaseNameParameter] = databaseName,
            [FilegroupNameParameter] = SqlServerE2eNames.MemoryOptimizedFilegroup(databaseName),
            [ContainerNameParameter] = SqlServerE2eNames.MemoryOptimizedContainer(databaseName),
            [ContainerDirectoryParameter] =
                SqlServerE2eNames.MemoryOptimizedContainerDirectory(databaseName)
        };

    /// <summary>
    /// The provisioning batch, run on <c>master</c> through the authorized bootstrap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The container's location is asked of SQL Server rather than assumed: the file is
    /// created on the server's own filesystem, which is frequently not the caller's
    /// machine, so a caller-local path would be meaningless. The batch prefers
    /// <c>SERVERPROPERTY('InstanceDefaultDataPath')</c> and falls back to the directory
    /// holding <c>master</c>'s own data file, which also settles the path separator for
    /// Windows and Linux instances alike.
    /// </para>
    /// <para>
    /// A database name, a filegroup name, and a file name cannot be parameters where T-SQL
    /// wants an identifier, so the <c>ALTER DATABASE</c> statements are composed and run
    /// through <c>sp_executesql</c>. Every value in them is quoted, and each one was
    /// generated from a database name already matched against the protected metadata record
    /// and the <see cref="SqlServerE2eNames.DatabasePrefix"/> grammar, which admits no
    /// quoting-relevant or path-relevant character. The container path is quoted with
    /// <c>REPLACE</c> rather than <c>QUOTENAME</c> because a directory path can exceed the
    /// 128 characters <c>QUOTENAME</c> accepts.
    /// </para>
    /// <para>
    /// The final check is what makes success mean provisioned: the batch confirms the
    /// filegroup and its container in the database's own catalog before it returns, so a
    /// silent partial provision cannot be reported as a ready database.
    /// </para>
    /// </remarks>
    public static string Script { get; } = $"""
        SET NOCOUNT ON;

        IF DB_ID({DatabaseNameParameter}) IS NULL
            THROW 51010, N'The E2E database to provision no longer exists.', 1;

        IF CONVERT(int, SERVERPROPERTY('IsXTPSupported')) <> 1
            THROW 51011, N'This SQL Server instance does not support memory-optimized tables.', 1;

        DECLARE @masterFile nvarchar(4000) =
        (
            SELECT TOP (1) physical_name
            FROM sys.master_files
            WHERE database_id = 1 AND [type] = 0
            ORDER BY file_id
        );

        IF @masterFile IS NULL
            THROW 51012, N'SQL Server reported no data-file location to derive a container path from.', 1;

        DECLARE @separator nchar(1) =
            CASE WHEN CHARINDEX(N'\', @masterFile) > 0 THEN N'\' ELSE N'/' END;

        DECLARE @dataPath nvarchar(4000) =
            CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultDataPath'));

        IF @dataPath IS NULL OR LEN(@dataPath) = 0
            SET @dataPath = LEFT(
                @masterFile,
                LEN(@masterFile) - CHARINDEX(@separator, REVERSE(@masterFile)) + 1);

        IF RIGHT(@dataPath, 1) <> @separator
            SET @dataPath = @dataPath + @separator;

        DECLARE @container nvarchar(4000) = @dataPath + {ContainerDirectoryParameter};

        DECLARE @sql nvarchar(max) =
            N'ALTER DATABASE ' + QUOTENAME({DatabaseNameParameter})
            + N' ADD FILEGROUP ' + QUOTENAME({FilegroupNameParameter})
            + N' CONTAINS MEMORY_OPTIMIZED_DATA;';
        EXEC sys.sp_executesql @sql;

        SET @sql =
            N'ALTER DATABASE ' + QUOTENAME({DatabaseNameParameter})
            + N' ADD FILE (NAME = N''' + REPLACE({ContainerNameParameter}, N'''', N'''''')
            + N''', FILENAME = N''' + REPLACE(@container, N'''', N'''''')
            + N''') TO FILEGROUP ' + QUOTENAME({FilegroupNameParameter}) + N';';
        EXEC sys.sp_executesql @sql;

        DECLARE @provisioned int;
        SET @sql =
            N'SELECT @found = COUNT(*)'
            + N' FROM ' + QUOTENAME({DatabaseNameParameter}) + N'.sys.database_files AS f'
            + N' INNER JOIN ' + QUOTENAME({DatabaseNameParameter}) + N'.sys.filegroups AS g'
            + N' ON g.data_space_id = f.data_space_id'
            + N' WHERE g.[type] = ''FX'';';
        EXEC sys.sp_executesql @sql, N'@found int OUTPUT', @found = @provisioned OUTPUT;

        IF @provisioned IS NULL OR @provisioned = 0
            THROW 51013, N'The memory-optimized filegroup and container were not created.', 1;
        """;
}
