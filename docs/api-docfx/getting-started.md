# Getting started

Choose the package that matches the layer you need:

- [ItTiger.TigerQuery](https://www.nuget.org/packages/ItTiger.TigerQuery/) is
  the standalone parser and execution engine.
- [ItTiger.TigerQuery.Core](https://www.nuget.org/packages/ItTiger.TigerQuery.Core/)
  provides named connection profiles and does not depend on the engine.
- [ItTiger.TigerQuery.CliCore](https://www.nuget.org/packages/ItTiger.TigerQuery.CliCore/)
  mounts connection-management commands in a TigerCli application.

The package READMEs remain the concise package-specific quickstarts. This guide
connects those entry points to the generated API reference.

## Run a SQL script

Install the engine:

```console
dotnet add package ItTiger.TigerQuery
```

Then configure and run a [TigerQueryEngine](xref:ItTiger.TigerQuery.Engine.TigerQueryEngine):

```csharp
using ItTiger.TigerQuery;
using ItTiger.TigerQuery.Engine;

var options = new TigerQueryEngineOptions
{
    ConnectionString =
        "Server=localhost;Database=master;Integrated Security=true",
    Mode = SqlCmdMode.SqlCmd,
    Variables = new Dictionary<string, string> { ["Env"] = "Dev" },
    OnMessage = (message, isException) => Console.WriteLine(message.Text),
    OnBatchEnd = end => Console.WriteLine(
        $"Batch {end.BatchNumber}: {(end.Success ? "ok" : "failed")}"),
    OnResultSet = resultSet =>
        Console.WriteLine($"{resultSet.Rows.Count} row(s)")
};

var engine = new TigerQueryEngine(options);
var result = await engine.RunFromStringAsync(
    """
    :setvar Greeting Hello
    PRINT '$(Greeting) from $(Env)';
    GO 2
    SELECT name FROM sys.databases;
    GO
    """);

Console.WriteLine(
    $"{result.ResultCode}: {result.ExecutedBatches} batch(es)");
```

Use `RunFromFileAsync(path)` for a script file or `RunAsync(TextReader)` for
another text source. All run methods accept a cancellation token.

> [!NOTE]
> The engine owns no console or presentation layer. Messages, batch progress,
> result sets, and prepared-plan information are delivered through callbacks
> on [TigerQueryEngineOptions](xref:ItTiger.TigerQuery.Engine.TigerQueryEngineOptions).

## Next steps

- Understand [prepared versus streaming execution](engine.md#prepared-versus-streaming-execution).
- Add named connections with [connection profiles](connection-profiles.md).
- Mount the profile commands in a [TigerCli application](cli-integration.md).
- Browse all published types in the [API reference](api-reference.md).

For package-oriented examples, see the repository READMEs for
[the engine](https://github.com/rkozlowski/TigerQuery/blob/main/ItTiger.TigerQuery/README.md),
[connection profiles](https://github.com/rkozlowski/TigerQuery/blob/main/ItTiger.TigerQuery.Core/README.md),
and [CLI integration](https://github.com/rkozlowski/TigerQuery/blob/main/ItTiger.TigerQuery.CliCore/README.md).
