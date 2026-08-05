using ItTiger.TigerQuery.Engine;
using ItTiger.TigerQuery.Events;
using Microsoft.Data.SqlClient;
using System.Text;

namespace ItTiger.TigerQuery.Tests.Helpers;

/// <summary>
/// Runs the engine against a scripted fake batch executor inside a private temporary
/// directory, so routing, file lifecycle, and CSV bytes can be asserted without SQL
/// Server.
/// </summary>
/// <remarks>
/// Result sets and messages travel the real delivery paths — the context's result-set
/// delivery and the engine's server-message reporting — so the router, destination
/// registry, and writers under test are the ones the engine actually uses.
/// </remarks>
internal sealed class OutputTestHost : IDisposable
{
    private readonly List<string> _events = [];
    private TigerQueryEngine? _engine;

    public OutputTestHost()
    {
        Directory = Path.Combine(
            Path.GetTempPath(),
            "tigerquery-output-tests",
            Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
    }

    /// <summary>Gets the private base directory used for relative output paths.</summary>
    public string Directory { get; }

    /// <summary>Gets the ordered lifecycle events observed during the run.</summary>
    public IReadOnlyList<string> Events => _events;

    /// <summary>Gets the result sets delivered to the application callback.</summary>
    public List<ResultSetInfo> ResultSetCallbacks { get; } = [];

    /// <summary>Gets the messages delivered to the application callback.</summary>
    public List<(SqlCmdMessage Message, bool IsException)> MessageCallbacks { get; } = [];

    /// <summary>Gets or sets what each batch execution emits.</summary>
    public Action<Emission>? Emit { get; set; }

    /// <summary>Gets the number of batch executions the fake executor performed.</summary>
    public int ExecutionCount { get; private set; }

    /// <summary>Gets the number of times the fake connection was opened.</summary>
    public int OpenCount { get; private set; }

    public string PathOf(string relativeName) => Path.Combine(Directory, relativeName);

    public bool Exists(string relativeName) => File.Exists(PathOf(relativeName));

    public byte[] ReadBytes(string relativeName) => File.ReadAllBytes(PathOf(relativeName));

    /// <summary>Reads a produced file as text, without treating the BOM as content.</summary>
    public string ReadText(string relativeName)
    {
        var bytes = ReadBytes(relativeName);
        var preamble = Encoding.UTF8.GetPreamble();
        return bytes.Length >= preamble.Length && bytes.Take(preamble.Length).SequenceEqual(preamble)
            ? Encoding.UTF8.GetString(bytes, preamble.Length, bytes.Length - preamble.Length)
            : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Lists the produced file names, ordered for stable assertions.</summary>
    public List<string> ProducedFiles()
    {
        return [.. System.IO.Directory
            .GetFiles(Directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Directory, path).Replace('\\', '/'))
            .OrderBy(name => name, StringComparer.Ordinal)];
    }

    public Task<ExecutionResult> RunAsync(
        string script,
        TigerQueryExecutionMode executionMode = TigerQueryExecutionMode.Streaming,
        OutputRoutingOptions? routing = null,
        Action<TigerQueryEngineOptions>? configure = null,
        SqlCmdMode mode = SqlCmdMode.SqlCmd)
    {
        return RunAsync(script, BuildOptions(executionMode, routing, configure, mode));
    }

    public async Task<ExecutionResult> RunAsync(string script, TigerQueryEngineOptions options)
    {
        var engine = CreateEngine(options);
        var result = await engine.RunFromStringAsync(script, TestContext.Current.CancellationToken);
        _events.Add("complete");
        return result;
    }

    /// <summary>Builds the engine without running it, for failure-path assertions.</summary>
    public TigerQueryEngine CreateEngine(TigerQueryEngineOptions options)
    {
        _engine = new TigerQueryEngine(options, OpenAsync, ExecuteAsync);
        return _engine;
    }

    public TigerQueryEngineOptions BuildOptions(
        TigerQueryExecutionMode executionMode = TigerQueryExecutionMode.Streaming,
        OutputRoutingOptions? routing = null,
        Action<TigerQueryEngineOptions>? configure = null,
        SqlCmdMode mode = SqlCmdMode.SqlCmd)
    {
        routing ??= new OutputRoutingOptions();
        if (routing.BaseDirectory is null)
        {
            routing = CloneWithBaseDirectory(routing, Directory);
        }

        var options = new TigerQueryEngineOptions
        {
            Mode = mode,
            ExecutionMode = executionMode,
            OutputRouting = routing,
            OnResultSet = result =>
            {
                ResultSetCallbacks.Add(result);
                _events.Add($"callback-result:{Describe(result)}");
            },
            OnMessage = (message, isException) =>
            {
                MessageCallbacks.Add((message, isException));
                _events.Add($"callback-message:{message.Severity}:{message.Text}");
            },
            OnBatchStart = batch => _events.Add($"start:{batch.BatchNumber}:{batch.ExecutionIndex}"),
            OnBatchEnd = batch => _events.Add($"end:{batch.BatchNumber}:{batch.ExecutionIndex}:{batch.Success}"),
            OnExecutionPlanReady = plan =>
                _events.Add($"plan:{plan.LogicalBatchCount}:{plan.TotalExecutionCount}")
        };

        configure?.Invoke(options);
        return options;
    }

    /// <summary>Copies routing options, replacing only the base directory.</summary>
    public static OutputRoutingOptions CloneWithBaseDirectory(OutputRoutingOptions source, string baseDirectory)
    {
        return new OutputRoutingOptions
        {
            InitialOutPath = source.InitialOutPath,
            InitialErrorPath = source.InitialErrorPath,
            BaseDirectory = baseDirectory,
            OutBehavior = source.OutBehavior,
            ResultSetFileMode = source.ResultSetFileMode,
            FileEncoding = source.FileEncoding,
            ResultSetFormat = source.ResultSetFormat,
            AllowScriptOutputDirectives = source.AllowScriptOutputDirectives
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leaked handle would fail the assertions that matter; cleanup is best effort.
        }
    }

    private static string Describe(ResultSetInfo result) =>
        $"{result.BatchNumber}:{result.ExecutionIndex}:{result.ResultSetIndex}";

    private Task OpenAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OpenCount++;
        _events.Add("open");
        return Task.CompletedTask;
    }

    private Task ExecuteAsync(
        QueryExecutionContext context,
        SqlBatch batch,
        int batchNumber,
        int executionIndex,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExecutionCount++;
        _events.Add($"execute:{batchNumber}:{executionIndex}");

        Emit?.Invoke(new Emission(this, context, _engine!, batch, batchNumber, executionIndex));
        return Task.CompletedTask;
    }

    /// <summary>What one fake batch execution produces.</summary>
    internal sealed class Emission
    {
        private readonly OutputTestHost _host;
        private readonly QueryExecutionContext _context;
        private readonly TigerQueryEngine _engine;
        private int _resultSetIndex;

        internal Emission(
            OutputTestHost host,
            QueryExecutionContext context,
            TigerQueryEngine engine,
            SqlBatch batch,
            int batchNumber,
            int executionIndex)
        {
            _host = host;
            _context = context;
            _engine = engine;
            Batch = batch;
            BatchNumber = batchNumber;
            ExecutionIndex = executionIndex;
        }

        public SqlBatch Batch { get; }

        public int BatchNumber { get; }

        public int ExecutionIndex { get; }

        /// <summary>Gets whether the batch text contains a marker.</summary>
        public bool Has(string marker) => Batch.Text.Contains(marker, StringComparison.Ordinal);

        /// <summary>Delivers one result set with the next result-set coordinate.</summary>
        public void ResultSet(string[] columns, params object?[][] rows)
        {
            _resultSetIndex++;
            var result = new ResultSetInfo
            {
                BatchNumber = BatchNumber,
                ExecutionIndex = ExecutionIndex,
                ResultSetIndex = _resultSetIndex,
                Columns = [.. columns.Select(name => new ColumnInfo { Name = name })],
                Rows = [.. rows]
            };

            _host._events.Add($"result:{BatchNumber}:{ExecutionIndex}:{_resultSetIndex}");
            _context.DeliverResultSet(result);
        }

        /// <summary>Delivers one SQL Server diagnostic through the engine's message path.</summary>
        public void ServerMessage(string text, byte severity = 0)
        {
            _host._events.Add($"message:{severity}");
            _engine.ReportServerMessage(new SqlCmdMessage
            {
                Text = text,
                Severity = severity,
                Number = 50000
            });
        }
    }
}
