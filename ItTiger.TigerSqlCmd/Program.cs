namespace ItTiger.TigerSqlCmd
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            // Compose splits off any `exec` child command line before TigerCli parses the rest.
            var (app, hostArguments) = TigerSqlCmdApp.Compose(args);

            return await app.RunAsync(hostArguments);
        }
    }
}
