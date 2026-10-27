using Chess.Server;
using Xunit;

namespace Chess.Server.Tests;

public class StoreTests
{
    [Fact]
    public void DatabaseUrlsBecomeConnectionStrings()
    {
        string local = PostgresStore.ToConnectionString("postgres://chess:p%40ss@localhost:5433/chess");
        Assert.Contains("Host=localhost", local);
        Assert.Contains("Port=5433", local);
        Assert.Contains("Database=chess", local);
        Assert.Contains("Username=chess", local);
        Assert.Contains("Password=p@ss", local);
        Assert.DoesNotContain("SSL", local);

        string hosted = PostgresStore.ToConnectionString("postgresql://u:pw@dpg-abc.oregon-postgres.render.com/db");
        Assert.Contains("Port=5432", hosted);
        Assert.Contains("SSL Mode=Require", hosted);

        Assert.Equal("Host=x;Database=y", PostgresStore.ToConnectionString("Host=x;Database=y"));
    }

    [Fact]
    public void FileStoreKeepsRecordsAcrossRestarts()
    {
        string folder = Path.Combine(Path.GetTempPath(), "store-" + Guid.NewGuid());
        try
        {
            var store = new FileStore(folder);
            Assert.Empty(store.Load("t"));
            store.Save("t", "a", "{\"x\":1}");
            store.Save("t", "b", "{\"x\":2}");
            store.Save("t", "a", "{\"x\":3}");
            store.Flush();
            var records = new FileStore(folder).Load("t");
            Assert.Equal(2, records.Count);
            Assert.Contains("3", records["a"]);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>Runs only when TEST_DATABASE_URL points at a PostgreSQL server to try.</summary>
    [Fact]
    public void PostgresStoreKeepsRecords()
    {
        string? url = Environment.GetEnvironmentVariable("TEST_DATABASE_URL");
        if (string.IsNullOrEmpty(url))
            return;
        string table = "test-" + Guid.NewGuid();
        var store = new PostgresStore(url);
        store.Save(table, "a", "{\"x\":1}");
        store.Save(table, "a", "{\"x\":2}");
        store.Flush();
        var records = new PostgresStore(url).Load(table);
        Assert.Single(records);
        Assert.Contains("2", records["a"]);
    }
}
