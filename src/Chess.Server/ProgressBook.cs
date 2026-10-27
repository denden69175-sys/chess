using System.Text.Json;
using System.Text.Json.Serialization;
using Chess.Core;

namespace Chess.Server;

/// <summary>A player's progress as the server keeps it, with the number of the last step applied.</summary>
public sealed class ProgressSave
{
    public PlayerProgress Progress { get; set; } = new();
    public int Seq { get; set; }

    /// <summary>The server's date of <see cref="Games"/>, yyyy-MM-dd.</summary>
    public string Day { get; set; } = "";

    /// <summary>Games that counted on <see cref="Day"/>.</summary>
    public int Games { get; set; }
}

/// <summary>
/// Everyone's coins, chests, skins and quests, by the player id the game keeps. The game sends each
/// change as a <see cref="ProgressAction"/>; the server replays games and puzzles before they
/// count, applies the step with the same rules, rolls the chests itself and sends its copy back,
/// which the game then uses. The game's own copy is never taken as it is. Not thread-safe:
/// <see cref="GameServer"/> calls it under its lock.
/// </summary>
public sealed class ProgressBook
{
    private const string Table = "progress";

    /// <summary>
    /// Games that count for quests per day. One person can play both sides of a game at one
    /// screen, or against a second account, so endless games must not mean endless coins.
    /// </summary>
    public const int GamesPerDay = 40;

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
    /// The player's progress, started empty on the first connection. What the game says it has
    /// is not taken: a save file is easy to edit, so only checked steps add up.
    /// </summary>
    public ProgressSave Sync(string id)
    {
        if (_saves.TryGetValue(id, out var save))
            return save;
        save = new ProgressSave { Progress = new PlayerProgress() };
        ProgressSync.Repair(save.Progress);
        _saves[id] = save;
        Save(id, save);
        return save;
    }

    /// <summary>
    /// Applies one step unless it was applied before. Games and puzzles are replayed first; a step
    /// that does not check out is skipped (its number still counts, so the queue moves on).
    /// Returns what a chest gave.
    /// </summary>
    public ChestResult? Apply(string id, ProgressAction action, DateTime today)
    {
        var save = Sync(id);
        if (action.seq <= save.Seq)
            return null;
        save.Seq = action.seq;
        var checkedAction = Check(save, action, today);
        var result = checkedAction == null ? null : ProgressSync.Apply(save.Progress, checkedAction, id, today, _random);
        Save(id, save);
        return result;
    }

    /// <summary>Counts an online game the server itself watched, with the result it saw.</summary>
    public void RecordOnline(string id, ProgressAction game, DateTime today)
    {
        var save = Sync(id);
        if (!CountGame(save, today))
            return;
        game.kind = ProgressAction.Game;
        game.online = true;
        game.bot = -1;
        ProgressSync.Apply(save.Progress, game, id, today);
        Save(id, save);
    }

    /// <summary>Counts one more game today, or says the day's limit is reached.</summary>
    private static bool CountGame(ProgressSave save, DateTime today)
    {
        string day = today.ToString("yyyy-MM-dd");
        if (save.Day != day)
        {
            save.Day = day;
            save.Games = 0;
        }
        if (save.Games >= GamesPerDay)
            return false;
        save.Games++;
        return true;
    }

    private static ProgressAction? Check(ProgressSave save, ProgressAction action, DateTime today)
    {
        switch (action.kind)
        {
            case ProgressAction.Game:
                // Online games are counted by the server when they end, never from the game's word.
                if (action.online)
                    return null;
                var game = GameCheck.Verify(action);
                return game != null && CountGame(save, today) ? game : null;
            case ProgressAction.Puzzle:
                return GameCheck.VerifyPuzzle(action) ? action : null;
            default:
                return action;
        }
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
