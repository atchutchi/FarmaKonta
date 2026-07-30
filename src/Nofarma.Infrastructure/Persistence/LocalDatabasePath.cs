using Microsoft.Data.Sqlite;

namespace Nofarma.Infrastructure.Persistence;

public static class LocalDatabasePath
{
    public static string GetDefault()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ABIPTOM",
            "Nofarma",
            "data");
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, "nofarma.db");
    }

    public static string BuildConnectionString(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            ForeignKeys = true,
            DefaultTimeout = 5,
            Pooling = true
        };

        return builder.ToString();
    }
}
