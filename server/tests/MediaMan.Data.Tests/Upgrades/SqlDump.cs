using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace MediaMan.Data.Tests.Upgrades;

/// <summary>
/// Writes a SQLite database as a plain SQL script (schema, then every row) and loads one back, so database fixtures are
/// text that can be reviewed in a diff and need no sqlite3 tool.
/// </summary>
public static class SqlDump
{
    /// <summary>The schema and rows of the database on <paramref name="connection"/>, tables and rows in a stable order.</summary>
    public static async Task<string> DumpAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var script = new StringBuilder();
        var tables = new List<string>();

        await using (var schema = connection.CreateCommand())
        {
            // EF's migration lock is transient, and sqlite_* objects belong to SQLite itself.
            schema.CommandText =
                """
                SELECT "type", "name", "sql" FROM sqlite_master
                WHERE "sql" IS NOT NULL AND "name" NOT LIKE 'sqlite_%' AND "tbl_name" <> '__EFMigrationsLock'
                ORDER BY CASE "type" WHEN 'table' THEN 0 ELSE 1 END, "name";
                """;
            await using var reader = await schema.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetString(0) == "table")
                {
                    tables.Add(reader.GetString(1));
                }

                script.Append(reader.GetString(2)).Append(";\n");
            }
        }

        foreach (var table in tables)
        {
            await using var rows = connection.CreateCommand();
            rows.CommandText = $"SELECT * FROM {Quote(table)} ORDER BY rowid;";
            await using var reader = await rows.ExecuteReaderAsync(cancellationToken);
            var columns = string.Join(", ", Enumerable.Range(0, reader.FieldCount).Select(i => Quote(reader.GetName(i))));
            while (await reader.ReadAsync(cancellationToken))
            {
                var values = string.Join(", ", Enumerable.Range(0, reader.FieldCount).Select(i => Literal(reader.GetValue(i))));
                script.Append(CultureInfo.InvariantCulture, $"INSERT INTO {Quote(table)} ({columns}) VALUES ({values});\n");
            }
        }

        return script.ToString();
    }

    /// <summary>Creates the database at <paramref name="databasePath"/> from <paramref name="script"/> in one transaction.</summary>
    public static async Task LoadAsync(string databasePath, string script, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_keys = OFF;\nBEGIN;\n{script}\nCOMMIT;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Quote(string name) => $"\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static string Literal(object value) => value switch
    {
        DBNull => "NULL",
        long l => l.ToString(CultureInfo.InvariantCulture),
        double d => RealLiteral(d),
        string s => $"'{s.Replace("'", "''", StringComparison.Ordinal)}'",
        byte[] b => $"X'{Convert.ToHexString(b)}'",
        _ => throw new NotSupportedException($"No SQL literal for a {value.GetType().Name}."),
    };

    private static string RealLiteral(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        return text.Contains('.', StringComparison.Ordinal) || text.Contains('E', StringComparison.Ordinal) ? text : text + ".0";
    }
}
