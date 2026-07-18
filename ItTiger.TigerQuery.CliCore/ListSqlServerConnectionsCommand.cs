using ItTiger.TigerCli.Commands;
using ItTiger.TigerCli.Enums;
using ItTiger.TigerCli.Primitives;
using ItTiger.TigerCli.Rendering;
using ItTiger.TigerCli.Terminal;

namespace ItTiger.TigerQuery.CliCore;

internal sealed class ListSqlServerConnectionsSettings : TigerCliSettings
{
    [TigerCliOption("--metadata",
        Description = "Match application metadata using key=value. Repeat to combine filters with AND.",
        DescriptionResourceKey = "Opt_Connection_ListMetadata_Description",
        ValueName = "key=value",
        Promptable = TigerCliPromptable.No)]
    public List<string> Metadata { get; set; } = [];

    [TigerCliOption("--metadata-set",
        Description = "Match profiles where an application metadata key is set.",
        DescriptionResourceKey = "Opt_Connection_MetadataSet_Description",
        ValueName = "key",
        Promptable = TigerCliPromptable.No)]
    public List<string> MetadataSet { get; set; } = [];

    [TigerCliOption("--metadata-not-set",
        Description = "Match profiles where an application metadata key is not set.",
        DescriptionResourceKey = "Opt_Connection_MetadataNotSet_Description",
        ValueName = "key",
        Promptable = TigerCliPromptable.No)]
    public List<string> MetadataNotSet { get; set; } = [];

    public override TigerCliValidationResult Validate()
    {
        var error = SqlServerConnectionMetadataOptions.ValidateFilters(
            Metadata,
            MetadataSet,
            MetadataNotSet);

        return error is null
            ? TigerCliValidationResult.Success()
            : TigerCliValidationResult.Error(T(error));
    }
}

internal sealed class ListSqlServerConnectionsCommand(SqlServerConnectionCommandContext context)
    : TigerCliAsyncCommandHandler<ListSqlServerConnectionsSettings, TigerCliExitKind>
{
    public override Task<TigerCliExitKind> ExecuteAsync(ListSqlServerConnectionsSettings s)
    {
        var metadataError = SqlServerConnectionMetadataOptions.ValidateFilters(
            s.Metadata,
            s.MetadataSet,
            s.MetadataNotSet);
        if (metadataError is not null)
        {
            SqlServerConnectionWriter.TryReportErrors(s, [metadataError]);
            return Task.FromResult(TigerCliExitKind.ValidationError);
        }

        var filters = SqlServerConnectionMetadataOptions.ToFilters(
            s.Metadata,
            s.MetadataSet,
            s.MetadataNotSet);
        var profiles = context.Store.QueryByMetadata(filters)
            .OrderBy(profile => profile.Name)
            .ToList();
        if (profiles.Count == 0)
        {
            TigerConsole.MarkupErrorLine(s.T("No SQL Server connections."));
            return Task.FromResult(TigerCliExitKind.Success);
        }

        var table = new CliTable()
            .ApplyPreset(CliTableStylePreset.Milano)
            .AddTitle(s.T("SQL Server connections"))
            .AddHeader(
                s.T("Name"),
                s.T("Server"),
                s.T("Authentication"),
                s.T("Database"));

        foreach (var profile in profiles)
        {
            table.AddRecord(
                profile.Name,
                profile.Server,
                profile.Authentication,
                profile.Database);
        }
        TigerConsole.Render(table);
        return Task.FromResult(TigerCliExitKind.Success);
    }
}
