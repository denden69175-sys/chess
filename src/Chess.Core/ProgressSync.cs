using System;
using System.Globalization;
using System.Linq;

namespace Chess.Core
{
    /// <summary>
    /// One change to a player's progress, as the game sends it to the server. The game applies it
    /// to its own copy too, so the server and the game run the same rules on the same steps.
    /// Public fields so Unity's JsonUtility and the server's JSON both read and write it.
    /// </summary>
    [Serializable]
    public sealed class ProgressAction
    {
        public const string Game = "game";
        public const string Puzzle = "puzzle";
        public const string Hint = "hint";
        public const string Claim = "claim";
        public const string Buy = "buy";
        public const string Open = "open";
        public const string Equip = "equip";

        /// <summary>Numbered by the game from 1 up, so a step sent twice after a lost connection counts once.</summary>
        public int seq;
        public string kind = "";

        /// <summary>The player's own date, yyyy-MM-dd, which picks the daily quests.</summary>
        public string day = "";

        // Game: what GameReport holds.
        public string mode = "";
        /// <summary>1 won, -1 lost, 0 draw or nobody was "you".</summary>
        public int won;
        /// <summary>The bot's level, or -1 when the opponent was not the bot.</summary>
        public int bot = -1;
        public bool online;
        public int captures;
        public int promotions;

        /// <summary>The puzzle's number (puzzle, hint) or the chest type (buy, open).</summary>
        public int number;

        /// <summary>The quest id (claim) or the skin id (equip).</summary>
        public string item = "";

        /// <summary>
        /// For a chest opened while the server was out of reach: the skin that came out. The server
        /// takes it as it is; with the server on line, chests are rolled by the server only.
        /// </summary>
        public string skin = "";

        public static ProgressAction ForGame(GameReport report) => new ProgressAction
        {
            kind = Game,
            mode = report.Mode,
            won = report.Won == true ? 1 : report.Won == false ? -1 : 0,
            bot = report.Bot.HasValue ? (int)report.Bot.Value : -1,
            online = report.Online,
            captures = report.Captures,
            promotions = report.Promotions,
        };

        public GameReport ToReport() => new GameReport
        {
            Mode = mode,
            Won = won > 0 ? true : won < 0 ? false : (bool?)null,
            Bot = bot >= 0 && bot <= (int)BotLevel.Hard ? (BotLevel)bot : (BotLevel?)null,
            Online = online,
            Captures = Math.Max(0, Math.Min(captures, 64)),
            Promotions = Math.Max(0, Math.Min(promotions, 16)),
        };
    }

    /// <summary>Applies <see cref="ProgressAction"/>s with the rules in <see cref="Rewards"/>.</summary>
    public static class ProgressSync
    {
        /// <summary>
        /// Applies one step. Returns what a chest gave (open), otherwise null. A step the rules do
        /// not allow (no coins, a quest not finished) changes nothing. <paramref name="random"/>
        /// rolls chests and is needed only for an open without <see cref="ProgressAction.skin"/>.
        /// </summary>
        public static ChestResult? Apply(PlayerProgress progress, ProgressAction action, string playerId, DateTime today, Random? random = null)
        {
            Repair(progress);
            Rewards.RefreshDaily(progress, DayOf(action.day, today), playerId);
            switch (action.kind)
            {
                case ProgressAction.Game:
                    Rewards.Record(progress, action.ToReport());
                    break;
                case ProgressAction.Puzzle:
                case ProgressAction.Hint:
                    var puzzle = PuzzleBook.Find(action.number);
                    if (puzzle != null && action.kind == ProgressAction.Puzzle)
                        Rewards.RecordPuzzle(progress, puzzle);
                    else if (puzzle != null)
                        Rewards.UseHint(progress, puzzle);
                    break;
                case ProgressAction.Claim:
                    var quest = Rewards.Quest(action.item);
                    if (quest != null)
                        Rewards.Claim(progress, quest);
                    break;
                case ProgressAction.Buy:
                    if (IsChest(action.number))
                        Rewards.Buy(progress, (ChestType)action.number);
                    break;
                case ProgressAction.Open:
                    if (!IsChest(action.number))
                        break;
                    if (action.skin.Length > 0)
                        return Rewards.OpenWith(progress, (ChestType)action.number, action.skin);
                    return Rewards.Open(progress, (ChestType)action.number, random ?? new Random());
                case ProgressAction.Equip:
                    Rewards.Equip(progress, action.item);
                    break;
            }
            return null;
        }

        /// <summary>The player's date when it is close to ours (time zones differ by a day at most), else ours.</summary>
        public static DateTime DayOf(string day, DateTime today)
        {
            if (DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && Math.Abs((date - today.Date).TotalDays) <= 1)
                return date;
            return today;
        }

        /// <summary>Fills in lists missing from an old or hand-made save, and drops skins that do not exist.</summary>
        public static void Repair(PlayerProgress progress)
        {
            if (progress.chests == null || progress.chests.Length < 3)
                Array.Resize(ref progress.chests, 3);
            int goals = Enum.GetValues(typeof(Goal)).Length;
            if (progress.totals == null || progress.totals.Length < goals)
                Array.Resize(ref progress.totals, goals);
            progress.owned ??= new System.Collections.Generic.List<string>();
            progress.modes ??= new System.Collections.Generic.List<string>();
            progress.solved ??= new System.Collections.Generic.List<int>();
            progress.hints ??= new System.Collections.Generic.List<int>();
            progress.daily ??= new System.Collections.Generic.List<QuestState>();
            progress.achievements ??= new System.Collections.Generic.List<string>();
            progress.questDay ??= "";
            progress.owned.RemoveAll(id => SkinCatalog.Find(id) == null);
            if (!progress.Owns(progress.pieces ?? ""))
                progress.pieces = SkinCatalog.DefaultPieces;
            if (!progress.Owns(progress.board ?? ""))
                progress.board = SkinCatalog.DefaultBoard;
        }

        private static bool IsChest(int number) => number >= 0 && number < 3;
    }
}
