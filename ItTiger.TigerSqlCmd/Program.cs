namespace ItTiger.TigerSqlCmd
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            var app = TigerSqlCmdApp.Build(TigerSqlCmdApp.CreateDefaultStore());

            return await app.RunAsync(args);
        }
    }
}
