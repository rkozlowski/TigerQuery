using ItTiger.TigerQuery.Engine;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ItTiger.TigerQuery.Tests.Helpers;

public static class TestHelper
{
    public static async Task<List<SqlBatch>> ParseBatchesAsync(string sql, TigerQueryEngineOptions options)
    {
        var (batches, _) = await ParseBatchesCtxAsync(sql, options);
        return batches;
    }
    public static async Task<(List<SqlBatch> Batches, QueryExecutionContext Context)> ParseBatchesCtxAsync(string sql, TigerQueryEngineOptions options)
    {
        var context = new QueryExecutionContext(options, new SqlConnection());
        using var reader = new StringReader(sql);
        var parser = new SqlCmdParser(reader, options, context);
        var batches = await parser.ReadBatchesAsync(TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        return (batches, context);
    }

    /// <summary>Reads the parser's internal ordered execution steps.</summary>
    internal static async Task<List<ExecutionStep>> ParseStepsAsync(
        string sql,
        TigerQueryEngineOptions? options = null)
    {
        var (steps, _) = await ParseStepsCtxAsync(sql, options);
        return steps;
    }

    internal static async Task<(List<ExecutionStep> Steps, QueryExecutionContext Context)> ParseStepsCtxAsync(
        string sql,
        TigerQueryEngineOptions? options = null)
    {
        options ??= new TigerQueryEngineOptions { Mode = SqlCmdMode.SqlCmd };
        var context = new QueryExecutionContext(options, new SqlConnection());
        using var reader = new StringReader(sql);
        var parser = new SqlCmdParser(reader, options, context);
        var steps = await parser
            .ReadExecutionStepsAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        return (steps, context);
    }

    /// <summary>Prepares a plan the same way the engine's prepared mode does.</summary>
    internal static async Task<PreparedExecutionPlan> PrepareAsync(
        string script,
        TigerQueryEngineOptions? options = null)
    {
        options ??= new TigerQueryEngineOptions { Mode = SqlCmdMode.SqlCmd };

        await using var connection = new SqlConnection();
        var context = new QueryExecutionContext(options, connection);
        using var reader = new StringReader(script);
        var parser = new SqlCmdParser(reader, options, context);

        return await TigerQueryEngine.PrepareExecutionPlanAsync(
            parser,
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Renders a step stream as short ordered tokens, so parser, plan, and execution
    /// order can be compared literally.
    /// </summary>
    internal static List<string> Describe(IEnumerable<ExecutionStep> steps)
    {
        return [.. steps.Select(step => step switch
        {
            ExecuteBatchStep batch =>
                $"batch:{batch.Execution.Batch.Text.Trim()}:{batch.Execution.Batch.ExecCount}",
            SetOutRouteStep route => $"out:{route.Directive.Path}",
            SetErrorRouteStep route => $"error:{route.Directive.Path}",
            _ => $"unknown:{step.GetType().Name}"
        })];
    }
}
