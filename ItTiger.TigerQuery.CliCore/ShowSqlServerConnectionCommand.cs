using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Enums;
using ItTiger.TigerCli.Primitives;
using ItTiger.TigerCli.Rendering;
using ItTiger.TigerCli.Terminal;

namespace ItTiger.TigerQuery.CliCore;

internal sealed class ShowSqlServerConnectionSettings : TigerCliSettings
{
    [TigerCliArgument(0, Name = "name", Description = "Connection name.",
        DescriptionResourceKey = "Arg_Connection_Name_Description", Provider = "connections")]
    public string Name { get; set; } = string.Empty;
}

internal sealed class ShowSqlServerConnectionCommand(SqlServerConnectionCommandContext context)
    : TigerCliAsyncCommandHandler<ShowSqlServerConnectionSettings, TigerCliExitKind>
{
    public override Task<TigerCliExitKind> ExecuteAsync(ShowSqlServerConnectionSettings s)
    {
        var profile = context.Store.Load()
            .FirstOrDefault(profile => profile.Name == s.Name);

        if (profile is null)
        {
            TigerConsole.MarkupErrorLine(s.E(
                "SQL Server connection [Value]{0}[/] was not found.",
                s.Name));

            return Task.FromResult(TigerCliExitKind.NotFound);
        }

        var details = new CliDetails()
            .ApplyPreset(CliTableStylePreset.Lucca)
            .AddTitle(s.T("SQL Server connection"))
            .Add(s.T("Name:"), profile.Name)
            .Add(s.T("Server:"), profile.Server)
            .Add(s.T("Authentication:"), profile.Authentication)
            .AddWhen(profile.Authentication == Core.AuthenticationType.SqlPassword,
                s.T("Username:"), profile.Username)
            .Add(s.T("Encrypt:"), profile.Encrypt)
            .AddOptional(s.T("Trust Server Certificate:"), profile.TrustServerCertificate)
            .AddOptional(s.T("Application Intent:"), profile.ApplicationIntent)
            .AddOptional(s.T("Database:"), profile.Database)
            .AddOptional(s.T("Connect Timeout:"), profile.ConnectTimeout)
            .AddOptional(s.T("Multi Subnet Failover:"), profile.MultiSubnetFailover)
            .AddOptional(s.T("Persist Security Info:"), profile.PersistSecurityInfo)
            .AddOptional(s.T("Pooling:"), profile.Pooling)
            .AddOptional(s.T("Min Pool Size:"), profile.MinPoolSize)
            .AddOptional(s.T("Max Pool Size:"), profile.MaxPoolSize);

        if (profile.Options != null)
        {
            foreach (var option in profile.Options)
                details.Add(option.Key, option.Value);
        }

        TigerConsole.Render(details);

        if (profile.Metadata.Count > 0)
        {
            var metadata = new CliTable()
                .ApplyPreset(CliTableStylePreset.Milano)
                .AddTitle(s.T("Metadata"))
                .AddHeader(s.T("Key"), s.T("Value"));

            foreach (var (key, value) in profile.Metadata.OrderBy(
                         item => item.Key,
                         StringComparer.Ordinal))
            {
                metadata.AddRecord(key, value);
            }

            TigerConsole.Render(metadata);
        }

        return Task.FromResult(TigerCliExitKind.Success);
    }
}
