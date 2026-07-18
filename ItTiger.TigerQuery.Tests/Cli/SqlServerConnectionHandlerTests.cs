using ItTiger.TigerCli.Commands;
using ItTiger.TigerQuery.CliCore;
using ItTiger.TigerQuery.Core;

namespace ItTiger.TigerQuery.Tests.Cli;

/// <summary>
/// Reusable-layer contract tests: CliCore handlers report portable TigerCli semantics
/// and never choose the consuming application's concrete process codes.
/// </summary>
[Collection(TigerCliAppCollection.Name)]
public sealed class SqlServerConnectionHandlerTests : IDisposable
{
    private readonly TempConnectionStore _temp = new();
    private readonly SqlServerConnectionCommandContext _context;

    public SqlServerConnectionHandlerTests()
    {
        _context = new SqlServerConnectionCommandContext(
            _temp.Store,
            SqlServerConnectionValidationPolicy.DatabaseOptional);
    }

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task List_ReturnsSuccess()
    {
        var result = await new ListSqlServerConnectionsCommand(_context)
            .ExecuteAsync(new ListSqlServerConnectionsSettings());

        Assert.Equal(TigerCliExitKind.Success, result);
    }

    [Fact]
    public async Task MetadataValidation_ReturnsPortableValidationErrorKind()
    {
        var addResult = await new AddSqlServerConnectionCommand(_context)
            .ExecuteAsync(new SqlServerConnectionSettings
            {
                Name = "demo",
                Server = "srv",
                Metadata = ["app.role=worker"],
                RemoveMetadata = ["app.role"]
            });
        var listResult = await new ListSqlServerConnectionsCommand(_context)
            .ExecuteAsync(new ListSqlServerConnectionsSettings
            {
                Metadata = ["missing-separator"]
            });

        Assert.Equal(TigerCliExitKind.ValidationError, addResult);
        Assert.Equal(TigerCliExitKind.ValidationError, listResult);
        Assert.Null(_temp.Store.Find("demo"));
    }

    [Fact]
    public async Task Add_ReturnsSuccess_ValidationError_AndAlreadyExists()
    {
        var handler = new AddSqlServerConnectionCommand(_context);

        var success = await handler.ExecuteAsync(ValidSettings());
        var duplicate = await handler.ExecuteAsync(ValidSettings());
        var invalid = await handler.ExecuteAsync(new SqlServerConnectionSettings
        {
            Name = "invalid",
            Server = "srv",
            Authentication = AuthenticationType.SqlPassword
        });

        Assert.Equal(TigerCliExitKind.Success, success);
        Assert.Equal(TigerCliExitKind.AlreadyExists, duplicate);
        Assert.Equal(TigerCliExitKind.ValidationError, invalid);
    }

    [Fact]
    public async Task Show_ReturnsSuccess_AndNotFound()
    {
        await new AddSqlServerConnectionCommand(_context).ExecuteAsync(ValidSettings());
        var handler = new ShowSqlServerConnectionCommand(_context);

        var success = await handler.ExecuteAsync(
            new ShowSqlServerConnectionSettings { Name = "demo" });
        var missing = await handler.ExecuteAsync(
            new ShowSqlServerConnectionSettings { Name = "missing" });

        Assert.Equal(TigerCliExitKind.Success, success);
        Assert.Equal(TigerCliExitKind.NotFound, missing);
    }

    [Fact]
    public async Task Edit_ReturnsSuccess_ValidationError_AndNotFound()
    {
        await new AddSqlServerConnectionCommand(_context).ExecuteAsync(ValidSettings());
        var handler = new EditSqlServerConnectionCommand(_context);

        var success = await handler.ExecuteAsync(new SqlServerConnectionSettings
        {
            Name = "demo",
            Server = "updated"
        });
        var invalid = await handler.ExecuteAsync(new SqlServerConnectionSettings
        {
            Name = "demo",
            Server = "updated",
            Authentication = AuthenticationType.SqlPassword
        });
        var missing = await handler.ExecuteAsync(new SqlServerConnectionSettings
        {
            Name = "missing",
            Server = "srv"
        });

        Assert.Equal(TigerCliExitKind.Success, success);
        Assert.Equal(TigerCliExitKind.ValidationError, invalid);
        Assert.Equal(TigerCliExitKind.NotFound, missing);
    }

    [Fact]
    public async Task Delete_ReturnsSuccess_AndNotFound()
    {
        await new AddSqlServerConnectionCommand(_context).ExecuteAsync(ValidSettings());
        var handler = new DeleteSqlServerConnectionCommand(_context);

        var success = await handler.ExecuteAsync(
            new DeleteSqlServerConnectionSettings { Name = "demo" });
        var missing = await handler.ExecuteAsync(
            new DeleteSqlServerConnectionSettings { Name = "missing" });

        Assert.Equal(TigerCliExitKind.Success, success);
        Assert.Equal(TigerCliExitKind.NotFound, missing);
    }

    private static SqlServerConnectionSettings ValidSettings() => new()
    {
        Name = "demo",
        Server = "srv"
    };
}
