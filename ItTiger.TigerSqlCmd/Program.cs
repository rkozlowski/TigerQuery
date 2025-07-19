using Spectre.Console.Cli;

namespace ItTiger.TigerSqlCmd
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            var app = new CommandApp();
            app.SetDefaultCommand<TigerSqlCmdCommand>();
            app.Configure(config =>
            {
                config.SetApplicationName("tiger-sqlcmd");
                config.AddCommand<TigerSqlCmdCommand>("run")
                
                      .WithDescription("Executes a SQL file or query using TigerQueryEngine.");
            });
            
            return await app.RunAsync(args);
        }
    }
}
