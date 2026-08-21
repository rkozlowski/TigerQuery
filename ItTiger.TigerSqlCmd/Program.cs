namespace ItTiger.TigerSqlCmd
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            // No argv preprocessing: TigerCli owns the `--` separator and binds the `exec`
            // child command line as raw trailing arguments.
            return await TigerSqlCmdApp.Build().RunAsync(args);
        }
    }
}
