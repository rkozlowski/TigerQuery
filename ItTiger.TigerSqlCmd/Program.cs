using ItTiger.TigerCli.Terminal;
using ItTiger.TigerCli.Tui.Themes;

namespace ItTiger.TigerSqlCmd
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            TigerConsole.CurrentTheme = new TigerBlueTheme();

            var app = TigerSqlCmdApp.Build(TigerSqlCmdApp.CreateDefaultStore());

            return await app.RunAsync(args);
        }
    }
}
