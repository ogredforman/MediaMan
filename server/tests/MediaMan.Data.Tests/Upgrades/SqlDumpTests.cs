using MediaMan.Fakes;

namespace MediaMan.Data.Tests.Upgrades;

public class SqlDumpTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_dump_loads_back_to_the_same_schema_and_values()
    {
        await using var source = TempDatabase.CreateEmpty();
        using (var connection = source.OpenRawConnection())
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE "T" ("Id" INTEGER PRIMARY KEY, "Text" TEXT, "Real" REAL, "Blob" BLOB, "Nothing" TEXT);
                CREATE INDEX "IX_T_Text" ON "T" ("Text");
                INSERT INTO "T" VALUES (1, 'it''s "quoted";
                two lines', 8.0, X'00FF10', NULL);
                INSERT INTO "T" VALUES (2, '', 0.1, X'', NULL);
                """;
            await command.ExecuteNonQueryAsync(Ct);
        }

        string dump;
        using (var connection = source.OpenRawConnection())
        {
            dump = await SqlDump.DumpAsync(connection, Ct);
        }

        await using var copy = TempDatabase.CreateEmpty();
        await SqlDump.LoadAsync(copy.Path, dump, Ct);

        using var reloaded = copy.OpenRawConnection();
        (await SqlDump.DumpAsync(reloaded, Ct)).Should().Be(dump);
        await using var read = reloaded.CreateCommand();
        read.CommandText = "SELECT \"Text\", typeof(\"Real\"), \"Real\", hex(\"Blob\"), \"Nothing\" FROM \"T\" WHERE \"Id\" = 1;";
        await using var reader = await read.ExecuteReaderAsync(Ct);
        (await reader.ReadAsync(Ct)).Should().BeTrue();
        reader.GetString(0).ReplaceLineEndings("\n").Should().Be("it's \"quoted\";\ntwo lines");
        reader.GetString(1).Should().Be("real");
        reader.GetDouble(2).Should().Be(8.0);
        reader.GetString(3).Should().Be("00FF10");
        reader.IsDBNull(4).Should().BeTrue();
    }
}
