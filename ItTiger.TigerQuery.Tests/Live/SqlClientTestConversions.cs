using ItTiger.TigerQuery.Core;
using Microsoft.Data.SqlClient;

namespace ItTiger.TigerQuery.Tests.Live;

internal static class SqlClientTestConversions
{
    /// <summary>Maps SqlClient encryption without relying on provider display aliases.</summary>
    internal static EncryptOption ToTigerQueryEncryptOption(SqlConnectionEncryptOption encrypt)
    {
        if (encrypt == SqlConnectionEncryptOption.Optional)
            return EncryptOption.Optional;
        if (encrypt == SqlConnectionEncryptOption.Mandatory)
            return EncryptOption.Mandatory;
        if (encrypt == SqlConnectionEncryptOption.Strict)
            return EncryptOption.Strict;

        throw new ArgumentOutOfRangeException(nameof(encrypt), encrypt, "Unsupported SqlClient encryption option.");
    }
}
