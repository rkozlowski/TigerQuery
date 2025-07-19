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
}
