using CsvHelper;
using CsvHelper.Configuration;
using ItTiger.TigerQuery.E2e;
using ItTiger.TigerQuery.Tests.Cli;
using ItTiger.TigerSqlCmd;
using System.Globalization;
using System.Text;

namespace ItTiger.TigerQuery.Tests.Live;

[Collection(LiveTestCollection.Name)]
public sealed class TigerSqlCmdCsvRoutingLiveTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "TigerSqlCmdCsvRoutingLiveTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ThreeBatchesRouteJoinedResultsToSeparateCsvFiles()
    {
        var configuration = SqlServerTestEnvironment.RequireConfiguration(
            requireDatabaseCreation: true);
        var lifecycle = new SqlServerE2eDatabaseLifecycle(
            configuration.Store,
            configuration.Resolution);
        Directory.CreateDirectory(directory);

        try
        {
            await lifecycle.CreateDatabaseAsync(TestContext.Current.CancellationToken);
            await lifecycle.RunSetupSqlAsync(
                """
                CREATE SCHEMA [Enum];
                GO

                CREATE TABLE [Enum].[Status]
                (
                    [Id] int NOT NULL CONSTRAINT [PK_Status] PRIMARY KEY,
                    [Name] nvarchar(30) NOT NULL
                );

                CREATE TABLE [dbo].[Customer]
                (
                    [Id] int NOT NULL CONSTRAINT [PK_Customer] PRIMARY KEY,
                    [Name] nvarchar(50) NOT NULL,
                    [StatusId] int NOT NULL,
                    CONSTRAINT [FK_Customer_Status] FOREIGN KEY ([StatusId])
                        REFERENCES [Enum].[Status] ([Id])
                );

                CREATE TABLE [dbo].[Project]
                (
                    [Id] int NOT NULL CONSTRAINT [PK_Project] PRIMARY KEY,
                    [Name] nvarchar(50) NOT NULL,
                    [StatusId] int NOT NULL,
                    CONSTRAINT [FK_Project_Status] FOREIGN KEY ([StatusId])
                        REFERENCES [Enum].[Status] ([Id])
                );

                CREATE TABLE [dbo].[WorkItem]
                (
                    [Id] int NOT NULL CONSTRAINT [PK_WorkItem] PRIMARY KEY,
                    [Name] nvarchar(50) NOT NULL,
                    [StatusId] int NOT NULL,
                    CONSTRAINT [FK_WorkItem_Status] FOREIGN KEY ([StatusId])
                        REFERENCES [Enum].[Status] ([Id])
                );

                INSERT INTO [Enum].[Status] ([Id], [Name]) VALUES
                    (1, N'Active'),
                    (2, N'Pending'),
                    (3, N'Archived');

                INSERT INTO [dbo].[Customer] ([Id], [Name], [StatusId]) VALUES
                    (20, N'Contoso', 2),
                    (10, N'Adventure Works', 1);

                INSERT INTO [dbo].[Project] ([Id], [Name], [StatusId]) VALUES
                    (200, N'Northwind migration', 3),
                    (100, N'CSV routing', 1);

                INSERT INTO [dbo].[WorkItem] ([Id], [Name], [StatusId]) VALUES
                    (3, N'Publish report', 1),
                    (1, N'Create schema', 3),
                    (2, N'Validate joins', 2);
                """,
                TestContext.Current.CancellationToken);

            var profileName = "csv-routing-" + Guid.NewGuid().ToString("N");
            lifecycle.AddDatabaseProfile(profileName);

            var scriptPath = Path.Combine(directory, "route-results.sql");
            await File.WriteAllTextAsync(
                scriptPath,
                """
                :Out customers.csv
                PRINT N'customers informational message';
                SELECT
                    customer.[Id],
                    customer.[Name],
                    status.[Name] AS [StatusName]
                FROM [dbo].[Customer] AS customer
                INNER JOIN [Enum].[Status] AS status ON status.[Id] = customer.[StatusId]
                ORDER BY customer.[Id];
                GO
                :Out projects.csv
                PRINT N'projects informational message';
                SELECT
                    project.[Id],
                    project.[Name],
                    status.[Name] AS [StatusName]
                FROM [dbo].[Project] AS project
                INNER JOIN [Enum].[Status] AS status ON status.[Id] = project.[StatusId]
                ORDER BY project.[Id];
                GO
                :Out work-items.csv
                PRINT N'work items informational message';
                SELECT
                    workItem.[Id],
                    workItem.[Name],
                    status.[Name] AS [StatusName]
                FROM [dbo].[WorkItem] AS workItem
                INNER JOIN [Enum].[Status] AS status ON status.[Id] = workItem.[StatusId]
                ORDER BY workItem.[Id];
                GO
                """,
                Encoding.UTF8,
                TestContext.Current.CancellationToken);

            var result = await TigerSqlCmdProcessRunner.RunAsync(
                new Dictionary<string, string?>(),
                directory,
                "run", "--non-interactive",
                "--connection", profileName,
                "--file", scriptPath,
                "--mode", "SqlCmdEx",
                "--format", "Csv",
                "--verbosity", "Silent");

            Assert.True(
                result.ExitCode == (int)TigerSqlCmdExitCode.Ok,
                result.StdErr + Environment.NewLine + result.StdOut);

            AssertCsvRecords(
                "customers.csv",
                ["Id", "Name", "StatusName"],
                ["10", "Adventure Works", "Active"],
                ["20", "Contoso", "Pending"]);
            AssertCsvRecords(
                "projects.csv",
                ["Id", "Name", "StatusName"],
                ["100", "CSV routing", "Active"],
                ["200", "Northwind migration", "Archived"]);
            AssertCsvRecords(
                "work-items.csv",
                ["Id", "Name", "StatusName"],
                ["1", "Create schema", "Archived"],
                ["2", "Validate joins", "Pending"],
                ["3", "Publish report", "Active"]);

            var csvFiles = Directory.GetFiles(directory, "*.csv", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            var expectedCsvFiles = new[] { "customers.csv", "projects.csv", "work-items.csv" }
                .Select(name => Path.GetFullPath(Path.Combine(directory, name)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            Assert.Equal(expectedCsvFiles, csvFiles);

            var ownedRoot = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
            Assert.All(
                csvFiles,
                path => Assert.StartsWith(ownedRoot, path, StringComparison.OrdinalIgnoreCase));

            var allFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            var expectedFiles = expectedCsvFiles
                .Append(Path.GetFullPath(scriptPath))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            Assert.Equal(expectedFiles, allFiles);

            await lifecycle.CleanupAsync(CancellationToken.None);
            Assert.True(lifecycle.DatabaseWasDropped);
            Assert.Null(configuration.Store.Find(profileName));
        }
        finally
        {
            if (lifecycle.CreatedDatabaseName is not null && !lifecycle.DatabaseWasDropped)
                await lifecycle.CleanupAsync(CancellationToken.None);
        }
    }

    private void AssertCsvRecords(string fileName, params string[][] expected)
    {
        var path = Path.Combine(directory, fileName);
        Assert.True(File.Exists(path), $"Expected CSV file '{path}' was not created.");

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            Delimiter = ",",
            NewLine = "\r\n"
        };
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, configuration);

        var records = new List<string[]>();
        while (csv.Read())
        {
            var record = new string[csv.Parser.Count];
            for (var index = 0; index < record.Length; index++)
                record[index] = csv.GetField(index) ?? string.Empty;
            records.Add(record);
        }

        Assert.Equal(expected, records);

        var text = File.ReadAllText(path, Encoding.UTF8);
        Assert.DoesNotContain("informational message", text, StringComparison.Ordinal);
        Assert.DoesNotContain("rows affected", text, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
