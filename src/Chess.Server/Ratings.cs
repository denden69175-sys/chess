using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chess.Server;

/// <summary>A player's rating card, kept between games.</summary>
public sealed class RatingRecord
{
    public string Name { get; set; } = "Игрок";
    public int Rating { get; set; } = Ratings.Start;
    public int Games { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
}

/// <summary>
/// Elo ratings of everyone who played a quick-match game, by the player id the client keeps.
/// With a file path the table is saved after every rated game and read back on start, so the
/// ratings live through a server restart. Not thread-safe: <see cref="GameServer"/> calls it
/// under its lock.
/// </summary>
public sealed class Ratings
{
    public const int Start = 1000;
    public const int K = 32;

    private readonly Dictionary<string, RatingRecord> _records;
    private readonly string? _path;

    public Ratings(string? path = null)
    {
        _path = path;
        _records = new Dictionary<string, RatingRecord>();
        if (path != null && File.Exists(path))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize(File.ReadAllText(path), RatingsJson.Default.DictionaryStringRatingRecord);
                if (loaded != null)
                    _records = loaded;
            }
            catch (JsonException)
            {
                // A broken file must not stop the server; it is overwritten after the next rated game.
            }
        }
    }

    /// <summary>The card for this id, created at <see cref="Start"/> on first use.</summary>
    public RatingRecord Get(string id, string? name = null)
    {
        if (!_records.TryGetValue(id, out var record))
            _records[id] = record = new RatingRecord();
        if (name != null)
            record.Name = name;
        return record;
    }

    /// <summary>The best players, highest rating first; only those who played at least one game.</summary>
    public List<RatingRecord> Top(int count) =>
        _records.Values.Where(r => r.Games > 0).OrderByDescending(r => r.Rating).ThenByDescending(r => r.Wins).Take(count).ToList();

    /// <summary>The expected score of a player rated <paramref name="a"/> against one rated <paramref name="b"/>.</summary>
    public static double Expected(double a, double b) => 1 / (1 + Math.Pow(10, (b - a) / 400));

    /// <summary>
    /// Applies one finished game. <paramref name="scores"/>[i, j] is player i's result against
    /// player j: 1 win, 0.5 draw, 0 loss, null when the two were on the same team.
    /// <paramref name="strength"/> is the rating each player is judged by (in team games the team's
    /// average). With more than one opponent K is shared between them. Returns each player's change.
    /// </summary>
    public int[] Apply(IReadOnlyList<string> ids, double?[,] scores, IReadOnlyList<double> strength)
    {
        int n = ids.Count;
        var deltas = new int[n];
        for (int i = 0; i < n; i++)
        {
            double change = 0;
            int opponents = 0;
            bool won = false, drew = true;
            for (int j = 0; j < n; j++)
            {
                if (i == j || scores[i, j] == null)
                    continue;
                double score = scores[i, j]!.Value;
                opponents++;
                change += score - Expected(strength[i], strength[j]);
                won |= score == 1;
                drew &= score == 0.5;
            }
            if (opponents == 0)
                continue;
            deltas[i] = (int)Math.Round(K * change / opponents);
            var record = Get(ids[i]);
            record.Rating = Math.Max(100, record.Rating + deltas[i]);
            record.Games++;
            if (won)
                record.Wins++;
            else if (drew)
                record.Draws++;
            else
                record.Losses++;
        }
        Save();
        return deltas;
    }

    private void Save()
    {
        if (_path == null)
            return;
        try
        {
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_records, RatingsJson.Default.DictionaryStringRatingRecord));
            File.Move(temp, _path, true);
        }
        catch (IOException)
        {
            // Ratings stay in memory; the next save tries again.
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, RatingRecord>))]
internal partial class RatingsJson : JsonSerializerContext
{
}
