using System.Collections.Concurrent;
using System.Text.Json;
using Npgsql;

namespace Chess.Server;

/// <summary>
/// Where the server keeps what must outlive a restart (ratings, player progress): records of JSON
/// text by table and player id. Writes go through a background queue so a slow disk or database
/// never holds up a game.
/// </summary>
public interface IStore
{
    /// <summary>Every record of a table, by id. Called once per table when the server starts.</summary>
    Dictionary<string, string> Load(string table);

    /// <summary>Saves one record; it is written in the background, in order.</summary>
    void Save(string table, string id, string json);

    /// <summary>Waits until everything saved so far is written.</summary>
    void Flush();
}

/// <summary>
/// Keeps each table in a JSON file in a folder, as an object of records by id, e.g. ratings.json.
/// Good for a server on a home computer.
/// </summary>
public sealed class FileStore : IStore
{
    private readonly string _folder;
    private readonly Dictionary<string, Dictionary<string, JsonElement>> _tables = new();
    private readonly BackgroundWriter _writer = new();

    public FileStore(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
    }

    private string PathOf(string table) => Path.Combine(_folder, table + ".json");

    public Dictionary<string, string> Load(string table)
    {
        var records = new Dictionary<string, JsonElement>();
        string path = PathOf(table);
        if (File.Exists(path))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var property in document.RootElement.EnumerateObject())
                    records[property.Name] = property.Value.Clone();
            }
            catch (JsonException)
            {
                // A broken file must not stop the server; it is overwritten on the next save.
            }
        }
        lock (_tables)
            _tables[table] = records;
        return records.ToDictionary(r => r.Key, r => r.Value.GetRawText());
    }

    public void Save(string table, string id, string json)
    {
        string text;
        lock (_tables)
        {
            if (!_tables.TryGetValue(table, out var records))
                _tables[table] = records = new Dictionary<string, JsonElement>();
            using (var document = JsonDocument.Parse(json))
                records[id] = document.RootElement.Clone();
            text = JsonSerializer.Serialize(records, StoreJson.Default.DictionaryStringJsonElement);
        }
        string path = PathOf(table);
        _writer.Enqueue(() =>
        {
            string temp = path + ".tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, path, true);
        });
    }

    public void Flush() => _writer.Flush();
}

/// <summary>
/// Keeps the tables in PostgreSQL, for hosts like Render where the server's own disk is wiped on
/// every restart. One table "records" holds everything.
/// </summary>
public sealed class PostgresStore : IStore
{
    private readonly NpgsqlDataSource _source;
    private readonly BackgroundWriter _writer = new();

    public PostgresStore(string connection)
    {
        _source = NpgsqlDataSource.Create(ToConnectionString(connection));
        using var command = _source.CreateCommand(
            "CREATE TABLE IF NOT EXISTS records (tbl text NOT NULL, id text NOT NULL, data text NOT NULL, PRIMARY KEY (tbl, id))");
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Hosts give the database as a URL (postgres://user:password@host:port/name); Npgsql wants
    /// key=value pairs. A string that is already key=value pairs is used as it is.
    /// </summary>
    public static string ToConnectionString(string connection)
    {
        if (!connection.StartsWith("postgres://") && !connection.StartsWith("postgresql://"))
            return connection;
        var uri = new Uri(connection);
        var user = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.Trim('/'),
            Username = Uri.UnescapeDataString(user[0]),
            Password = user.Length > 1 ? Uri.UnescapeDataString(user[1]) : null,
        };
        // Databases outside the host's own network require SSL; a local one usually has none.
        if (uri.Host != "localhost" && uri.Host != "127.0.0.1" && uri.Host.Contains('.'))
            builder.SslMode = SslMode.Require;
        return builder.ConnectionString;
    }

    public Dictionary<string, string> Load(string table)
    {
        var records = new Dictionary<string, string>();
        using var command = _source.CreateCommand("SELECT id, data FROM records WHERE tbl = $1");
        command.Parameters.AddWithValue(table);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            records[reader.GetString(0)] = reader.GetString(1);
        return records;
    }

    public void Save(string table, string id, string json) =>
        _writer.Enqueue(() =>
        {
            using var command = _source.CreateCommand(
                "INSERT INTO records (tbl, id, data) VALUES ($1, $2, $3) ON CONFLICT (tbl, id) DO UPDATE SET data = EXCLUDED.data");
            command.Parameters.AddWithValue(table);
            command.Parameters.AddWithValue(id);
            command.Parameters.AddWithValue(json);
            command.ExecuteNonQuery();
        });

    public void Flush() => _writer.Flush();
}

/// <summary>Runs writes one at a time on a background thread; a failed write is logged and skipped.</summary>
internal sealed class BackgroundWriter
{
    private readonly BlockingCollection<Action> _queue = new();
    private int _pending;

    public BackgroundWriter()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "store writer" };
        thread.Start();
    }

    public void Enqueue(Action write)
    {
        Interlocked.Increment(ref _pending);
        _queue.Add(write);
    }

    public void Flush()
    {
        while (Volatile.Read(ref _pending) > 0)
            Thread.Sleep(5);
    }

    private void Run()
    {
        foreach (var write in _queue.GetConsumingEnumerable())
        {
            try
            {
                write();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Could not save: " + e.Message);
            }
            finally
            {
                Interlocked.Decrement(ref _pending);
            }
        }
    }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal partial class StoreJson : System.Text.Json.Serialization.JsonSerializerContext
{
}
