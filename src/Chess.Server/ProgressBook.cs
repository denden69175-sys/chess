using System.Text.Json;
using System.Text.Json.Serialization;
using Chess.Core;

namespace Chess.Server;

/// <summary>A player's progress as the server keeps it, with the number of the last step applied.</summary>
public sealed class ProgressSave
{
    public PlayerProgress Progress { get; set; } = new();
    public int Seq { get; set; }
}

/// <summary>
/// Everyone's coins, chests, skins and quests, by the player id the game keeps. The game sends each
/// change as a <see cref="ProgressAction"/>; the server applies it with the same rules, rolls the
/// chests itself and sends its copy back, which the game then uses. Not thread-safe:
/// <see cref="GameServer"/> calls it under its lock.
/// </summary>
public sealed class ProgressBook
{
    private const string Table = "progress";
    private readonly Dictionary<string, ProgressSave> _saves = new();
    private readonly IStore? _store;
    private readonly Random _random;

    public ProgressBook(IStore? store = null, Random? random = null)
    {
        _store = store;
        _random = random ?? new Random();
        if (store == null)
            return;
        foreach (var (id, json) in store.Load(Table))
        {
            var save = Parse(json, ProgressJson.Default.ProgressSave);
            if (save != null)
            {
                ProgressSync.Repair(save.Progress);
                _saves[id] = save;
            }
        }
    }

    public ProgressSave? Find(string id) => _saves.TryGetValue(id, out var save) ? save : null;

    /// <summary>
    /// The game's first word after connecting: its own copy, used only when the server has none
    /// for this player yet (the first connection, or progress made before there was a server).
    /// </summary>
    public ProgressSave Sync(string id, string? json)
    {
        if (_saves.TryGetValue(id, out var save))
            return save;
        var progress = json == null ? null : Parse(json, ProgressJson.Default.PlayerProgress);
        save = new ProgressSave { Progress = progress ?? new PlayerProgress() };
        ProgressSync.Repair(save.Progress);
        _saves[id] = save;
        Save(id, save);
        return save;
    }

    /// <summary>Applies one step unless it was applied before. Returns what a chest gave.</summary>
    public ChestResult? Apply(string id, ProgressAction action, DateTime today)
    {
        var save = Sync(id, null);
        if (action.seq <= save.Seq)
            return null;
        save.Seq = action.seq;
        var result = ProgressSync.Apply(save.Progress, action, id, today, _random);
        Save(id, save);
        return result;
    }

    public static string ToJson(PlayerProgress progress) => JsonSerializer.Serialize(progress, ProgressJson.Default.PlayerProgress);

    public static ProgressAction? ParseAction(string? json) => json == null ? null : Parse(json, ProgressJson.Default.ProgressAction);

    private void Save(string id, ProgressSave save) =>
        _store?.Save(Table, id, JsonSerializer.Serialize(save, ProgressJson.Default.ProgressSave));

    private static T? Parse<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(json, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>The game's classes keep their data in public fields (for Unity), so fields are included.</summary>
[JsonSourceGenerationOptions(IncludeFields = true)]
[JsonSerializable(typeof(ProgressSave))]
[JsonSerializable(typeof(PlayerProgress))]
[JsonSerializable(typeof(ProgressAction))]
internal partial class ProgressJson : JsonSerializerContext
{
}
