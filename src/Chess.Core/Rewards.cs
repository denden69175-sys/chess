using System;
using System.Collections.Generic;
using System.Linq;

namespace Chess.Core
{
    public enum Rarity
    {
        Common,
        Rare,
        Epic,
        Legendary,
    }

    public enum SkinKind
    {
        Pieces,
        Board,
    }

    public enum ChestType
    {
        Common,
        Rare,
        Epic,
    }

    /// <summary>A colouring for the pieces or the board. How it looks is up to the client; here it is a name and a rarity.</summary>
    public sealed class Skin
    {
        public Skin(string id, string name, SkinKind kind, Rarity rarity)
        {
            Id = id;
            Name = name;
            Kind = kind;
            Rarity = rarity;
        }

        public string Id { get; }
        public string Name { get; }
        public SkinKind Kind { get; }
        public Rarity Rarity { get; }
    }

    public static class SkinCatalog
    {
        public const string DefaultPieces = "classic";
        public const string DefaultBoard = "wood";

        public static readonly IReadOnlyList<Skin> All = new[]
        {
            new Skin(DefaultPieces, "Классика", SkinKind.Pieces, Rarity.Common),
            new Skin("ivory", "Слоновая кость", SkinKind.Pieces, Rarity.Common),
            new Skin("mint", "Мята", SkinKind.Pieces, Rarity.Common),
            new Skin("sand", "Песок", SkinKind.Pieces, Rarity.Common),
            new Skin("graphite", "Графит", SkinKind.Pieces, Rarity.Common),
            new Skin("ice", "Лёд", SkinKind.Pieces, Rarity.Rare),
            new Skin("emerald", "Изумруд", SkinKind.Pieces, Rarity.Rare),
            new Skin("ruby", "Рубин", SkinKind.Pieces, Rarity.Rare),
            new Skin("copper", "Медь", SkinKind.Pieces, Rarity.Rare),
            new Skin("gold", "Золото и серебро", SkinKind.Pieces, Rarity.Epic),
            new Skin("neon", "Неон", SkinKind.Pieces, Rarity.Epic),
            new Skin("lava", "Лава", SkinKind.Pieces, Rarity.Epic),
            new Skin("rainbow", "Радуга", SkinKind.Pieces, Rarity.Legendary),
            new Skin("cosmos", "Космос", SkinKind.Pieces, Rarity.Legendary),
            new Skin("ghost", "Призраки", SkinKind.Pieces, Rarity.Legendary),

            new Skin(DefaultBoard, "Дерево", SkinKind.Board, Rarity.Common),
            new Skin("tournament", "Турнирная", SkinKind.Board, Rarity.Common),
            new Skin("stone", "Камень", SkinKind.Board, Rarity.Common),
            new Skin("sky", "Небо", SkinKind.Board, Rarity.Common),
            new Skin("marble", "Мрамор", SkinKind.Board, Rarity.Rare),
            new Skin("forest", "Лес", SkinKind.Board, Rarity.Rare),
            new Skin("chocolate", "Шоколад", SkinKind.Board, Rarity.Rare),
            new Skin("night", "Ночь", SkinKind.Board, Rarity.Epic),
            new Skin("sakura", "Сакура", SkinKind.Board, Rarity.Epic),
            new Skin("desert", "Пустыня", SkinKind.Board, Rarity.Epic),
            new Skin("neongrid", "Неоновая сетка", SkinKind.Board, Rarity.Legendary),
            new Skin("royal", "Королевская", SkinKind.Board, Rarity.Legendary),
        };

        public static Skin? Find(string? id) => All.FirstOrDefault(s => s.Id == id);

        public static bool IsDefault(string id) => id == DefaultPieces || id == DefaultBoard;

        public static string RarityName(Rarity rarity) => rarity switch
        {
            Rarity.Common => "Обычная",
            Rarity.Rare => "Редкая",
            Rarity.Epic => "Эпическая",
            _ => "Легендарная",
        };

        public static string ChestName(ChestType chest) => chest switch
        {
            ChestType.Common => "Обычный сундук",
            ChestType.Rare => "Редкий сундук",
            _ => "Эпический сундук",
        };
    }

    /// <summary>What a quest or a puzzle gives: a chest, coins or both.</summary>
    public readonly struct Reward
    {
        public Reward(ChestType? chest, int coins = 0)
        {
            Chest = chest;
            Coins = coins;
        }

        public ChestType? Chest { get; }
        public int Coins { get; }

        public override string ToString()
        {
            var parts = new List<string>();
            if (Chest.HasValue)
                parts.Add(SkinCatalog.ChestName(Chest.Value).ToLowerInvariant());
            if (Coins > 0)
                parts.Add(Coins + " монет");
            return string.Join(" и ", parts);
        }
    }

    /// <summary>What a quest counts.</summary>
    public enum Goal
    {
        PlayGames,
        WinGames,
        WinBotEasy,
        WinBotMedium,
        WinBotHard,
        WinOnline,
        Captures,
        Promotions,
        Puzzles,
        PlayTimeTravel,
        PlayFiveD,
        PlayArmy,
        PlayParty,
        ModesPlayed,
    }

    public sealed class QuestDef
    {
        public QuestDef(string id, string text, Goal goal, int target, Reward reward)
        {
            Id = id;
            Text = text;
            Goal = goal;
            Target = target;
            Reward = reward;
        }

        public string Id { get; }
        public string Text { get; }
        public Goal Goal { get; }
        public int Target { get; }
        public Reward Reward { get; }
    }

    /// <summary>One finished game, as the quests see it.</summary>
    public sealed class GameReport
    {
        /// <summary>"classic", "timetravel", "5d", "army", "team" or "three".</summary>
        public string Mode { get; set; } = "classic";

        /// <summary>True for a win, false for a loss, null for a draw or a game where nobody was "you".</summary>
        public bool? Won { get; set; }

        public BotLevel? Bot { get; set; }
        public bool Online { get; set; }

        /// <summary>Pieces the player took.</summary>
        public int Captures { get; set; }

        /// <summary>Pawns the player promoted.</summary>
        public int Promotions { get; set; }
    }

    public sealed class ChestResult
    {
        public ChestResult(Skin skin, bool duplicate, int coins)
        {
            Skin = skin;
            Duplicate = duplicate;
            Coins = coins;
        }

        public Skin Skin { get; }

        /// <summary>The skin was already in the collection; it turned into <see cref="Coins"/>.</summary>
        public bool Duplicate { get; }
        public int Coins { get; }
    }

    /// <summary>One active daily quest.</summary>
    [Serializable]
    public sealed class QuestState
    {
        public string id = "";
        public int progress;
        public bool claimed;
    }

    /// <summary>
    /// Everything the player earned. Plain public fields so Unity's JsonUtility can save it; the
    /// rules live in <see cref="Rewards"/>.
    /// </summary>
    [Serializable]
    public sealed class PlayerProgress
    {
        public int coins;
        public List<string> owned = new List<string>();
        public string pieces = SkinCatalog.DefaultPieces;
        public string board = SkinCatalog.DefaultBoard;
        /// <summary>Unopened chests by <see cref="ChestType"/>.</summary>
        public int[] chests = new int[3];
        /// <summary>Chests opened since the last legendary skin.</summary>
        public int sinceLegendary;
        public int chestsOpened;
        /// <summary>Lifetime totals by <see cref="Goal"/>.</summary>
        public int[] totals = new int[Enum.GetValues(typeof(Goal)).Length];
        public List<string> modes = new List<string>();
        public List<int> solved = new List<int>();
        public string questDay = "";
        public List<QuestState> daily = new List<QuestState>();
        public List<string> achievements = new List<string>();

        public bool Owns(string skin) => SkinCatalog.IsDefault(skin) || owned.Contains(skin);

        public int Total(Goal goal) => (int)goal < totals.Length ? totals[(int)goal] : 0;
    }

    /// <summary>
    /// Quests, achievements and chests. Chests are earned only by playing, never bought with money.
    /// Every chest has a chance of a legendary skin, and the 30th chest in a row without one gives
    /// one for sure.
    /// </summary>
    public static class Rewards
    {
        public const int DailyCount = 3;
        public const int Pity = 30;

        public static readonly IReadOnlyList<QuestDef> DailyPool = new[]
        {
            new QuestDef("d_play3", "Сыграй 3 партии", Goal.PlayGames, 3, new Reward(null, 40)),
            new QuestDef("d_win", "Выиграй партию", Goal.WinGames, 1, new Reward(ChestType.Common)),
            new QuestDef("d_hard", "Победи сложного бота", Goal.WinBotHard, 1, new Reward(ChestType.Rare)),
            new QuestDef("d_capture", "Съешь 20 фигур", Goal.Captures, 20, new Reward(null, 50)),
            new QuestDef("d_promote", "Проведи пешку в ферзи", Goal.Promotions, 1, new Reward(null, 60)),
            new QuestDef("d_puzzles3", "Реши 3 задачи", Goal.Puzzles, 3, new Reward(ChestType.Common)),
            new QuestDef("d_puzzle", "Реши задачу", Goal.Puzzles, 1, new Reward(null, 30)),
            new QuestDef("d_time", "Сыграй с путешествием во времени", Goal.PlayTimeTravel, 1, new Reward(null, 40)),
            new QuestDef("d_5d", "Сыграй партию в 5D", Goal.PlayFiveD, 1, new Reward(ChestType.Common)),
            new QuestDef("d_army", "Сыграй своей армией", Goal.PlayArmy, 1, new Reward(null, 40)),
            new QuestDef("d_party", "Сыграй в «Союзников» или «Тройной трон»", Goal.PlayParty, 1, new Reward(null, 50)),
            new QuestDef("d_online", "Победи в онлайне", Goal.WinOnline, 1, new Reward(ChestType.Rare)),
        };

        public static readonly IReadOnlyList<QuestDef> Achievements = new[]
        {
            new QuestDef("a_win", "Первая победа", Goal.WinGames, 1, new Reward(ChestType.Common)),
            new QuestDef("a_easy", "Победи лёгкого бота", Goal.WinBotEasy, 1, new Reward(ChestType.Common)),
            new QuestDef("a_medium", "Победи среднего бота", Goal.WinBotMedium, 1, new Reward(ChestType.Common, 50)),
            new QuestDef("a_hard", "Победи сложного бота", Goal.WinBotHard, 1, new Reward(ChestType.Rare)),
            new QuestDef("a_puzzles10", "Реши 10 задач", Goal.Puzzles, 10, new Reward(ChestType.Rare)),
            new QuestDef("a_puzzles_all", "Реши все задачи", Goal.Puzzles, PuzzleBook.All.Count, new Reward(ChestType.Epic)),
            new QuestDef("a_games25", "Сыграй 25 партий", Goal.PlayGames, 25, new Reward(ChestType.Rare)),
            new QuestDef("a_capture100", "Съешь 100 фигур", Goal.Captures, 100, new Reward(ChestType.Rare)),
            new QuestDef("a_online5", "Победи в онлайне 5 раз", Goal.WinOnline, 5, new Reward(ChestType.Epic)),
            new QuestDef("a_modes", "Сыграй во всех 6 режимах", Goal.ModesPlayed, 6, new Reward(ChestType.Epic)),
        };

        public static QuestDef? Quest(string id) =>
            DailyPool.FirstOrDefault(q => q.Id == id) ?? Achievements.FirstOrDefault(q => q.Id == id);

        /// <summary>Coins for a chest bought in the shop.</summary>
        public static int Price(ChestType chest) => chest switch
        {
            ChestType.Common => 120,
            ChestType.Rare => 300,
            _ => 700,
        };

        /// <summary>Coins for a skin that was already in the collection.</summary>
        public static int DuplicateCoins(Rarity rarity) => rarity switch
        {
            Rarity.Common => 15,
            Rarity.Rare => 40,
            Rarity.Epic => 100,
            _ => 250,
        };

        /// <summary>Chance of each rarity in percent: common, rare, epic, legendary.</summary>
        public static int[] Odds(ChestType chest) => chest switch
        {
            ChestType.Common => new[] { 70, 24, 5, 1 },
            ChestType.Rare => new[] { 35, 45, 16, 4 },
            _ => new[] { 0, 50, 40, 10 },
        };

        /// <summary>
        /// Starts a new set of daily quests when the day changed. The set depends on the day and the
        /// player, so it stays the same all day. Returns true when a new set began.
        /// </summary>
        public static bool RefreshDaily(PlayerProgress progress, DateTime today, string playerId)
        {
            string day = today.ToString("yyyy-MM-dd");
            if (progress.questDay == day && progress.daily.Count == DailyCount)
                return false;
            progress.questDay = day;
            progress.daily.Clear();
            var random = new Random(StableHash(day + playerId));
            var pool = DailyPool.ToList();
            for (int i = 0; i < DailyCount && pool.Count > 0; i++)
            {
                int pick = random.Next(pool.Count);
                progress.daily.Add(new QuestState { id = pool[pick].Id });
                pool.RemoveAt(pick);
            }
            return true;
        }

        /// <summary>Counts a finished game for the totals and the daily quests.</summary>
        public static void Record(PlayerProgress progress, GameReport game)
        {
            Add(progress, Goal.PlayGames, 1);
            if (game.Won == true)
            {
                Add(progress, Goal.WinGames, 1);
                if (game.Online)
                    Add(progress, Goal.WinOnline, 1);
                if (game.Bot == BotLevel.Easy)
                    Add(progress, Goal.WinBotEasy, 1);
                else if (game.Bot == BotLevel.Medium)
                    Add(progress, Goal.WinBotMedium, 1);
                else if (game.Bot == BotLevel.Hard)
                    Add(progress, Goal.WinBotHard, 1);
            }
            Add(progress, Goal.Captures, game.Captures);
            Add(progress, Goal.Promotions, game.Promotions);
            switch (game.Mode)
            {
                case "timetravel":
                    Add(progress, Goal.PlayTimeTravel, 1);
                    break;
                case "5d":
                    Add(progress, Goal.PlayFiveD, 1);
                    break;
                case "army":
                    Add(progress, Goal.PlayArmy, 1);
                    break;
                case "team":
                case "three":
                    Add(progress, Goal.PlayParty, 1);
                    break;
            }
            if (!progress.modes.Contains(game.Mode))
            {
                progress.modes.Add(game.Mode);
                Add(progress, Goal.ModesPlayed, 1);
            }
        }

        /// <summary>
        /// Counts a solved puzzle. The first solve of each puzzle pays its coins and counts for the
        /// quests; solving it again is just practice. Returns the coins paid.
        /// </summary>
        public static int RecordPuzzle(PlayerProgress progress, Puzzle puzzle)
        {
            if (progress.solved.Contains(puzzle.Number))
                return 0;
            progress.solved.Add(puzzle.Number);
            progress.coins += puzzle.Reward;
            Add(progress, Goal.Puzzles, 1);
            return puzzle.Reward;
        }

        private static void Add(PlayerProgress progress, Goal goal, int amount)
        {
            if (amount <= 0)
                return;
            int count = Enum.GetValues(typeof(Goal)).Length;
            if (progress.totals.Length < count)
                Array.Resize(ref progress.totals, count);
            progress.totals[(int)goal] += amount;
            foreach (var state in progress.daily)
            {
                var quest = Quest(state.id);
                if (quest != null && quest.Goal == goal && !state.claimed)
                    state.progress = Math.Min(quest.Target, state.progress + amount);
            }
        }

        public static int Progress(PlayerProgress progress, QuestDef quest)
        {
            if (Achievements.Contains(quest))
                return Math.Min(quest.Target, progress.Total(quest.Goal));
            var state = progress.daily.FirstOrDefault(s => s.id == quest.Id);
            return state?.progress ?? 0;
        }

        public static bool IsClaimed(PlayerProgress progress, QuestDef quest) =>
            Achievements.Contains(quest)
                ? progress.achievements.Contains(quest.Id)
                : progress.daily.Any(s => s.id == quest.Id && s.claimed);

        public static bool CanClaim(PlayerProgress progress, QuestDef quest) =>
            !IsClaimed(progress, quest) && Progress(progress, quest) >= quest.Target
            && (Achievements.Contains(quest) || progress.daily.Any(s => s.id == quest.Id));

        /// <summary>Takes a finished quest's reward. Returns false when it is not finished or already taken.</summary>
        public static bool Claim(PlayerProgress progress, QuestDef quest)
        {
            if (!CanClaim(progress, quest))
                return false;
            if (Achievements.Contains(quest))
                progress.achievements.Add(quest.Id);
            else
                progress.daily.First(s => s.id == quest.Id).claimed = true;
            Give(progress, quest.Reward);
            return true;
        }

        public static void Give(PlayerProgress progress, Reward reward)
        {
            if (progress.chests.Length < 3)
                Array.Resize(ref progress.chests, 3);
            if (reward.Chest.HasValue)
                progress.chests[(int)reward.Chest.Value]++;
            progress.coins += reward.Coins;
        }

        public static bool Buy(PlayerProgress progress, ChestType chest)
        {
            int price = Price(chest);
            if (progress.coins < price)
                return false;
            progress.coins -= price;
            Give(progress, new Reward(chest));
            return true;
        }

        /// <summary>
        /// Opens one chest of this type, if the player has one. A skin not yet in the collection is
        /// preferred; when every skin of the rolled rarity is owned, the duplicate turns into coins.
        /// </summary>
        public static ChestResult? Open(PlayerProgress progress, ChestType chest, Random random)
        {
            if (progress.chests.Length < 3 || progress.chests[(int)chest] <= 0)
                return null;
            progress.chests[(int)chest]--;
            progress.chestsOpened++;

            Rarity rarity;
            if (progress.sinceLegendary + 1 >= Pity)
            {
                rarity = Rarity.Legendary;
            }
            else
            {
                var odds = Odds(chest);
                int roll = random.Next(100);
                int r = 0;
                while (r < 3 && roll >= odds[r])
                {
                    roll -= odds[r];
                    r++;
                }
                rarity = (Rarity)r;
            }
            progress.sinceLegendary = rarity == Rarity.Legendary ? 0 : progress.sinceLegendary + 1;

            var ofRarity = SkinCatalog.All.Where(s => s.Rarity == rarity && !SkinCatalog.IsDefault(s.Id)).ToList();
            var fresh = ofRarity.Where(s => !progress.Owns(s.Id)).ToList();
            if (fresh.Count > 0)
            {
                var skin = fresh[random.Next(fresh.Count)];
                progress.owned.Add(skin.Id);
                return new ChestResult(skin, false, 0);
            }
            var duplicate = ofRarity[random.Next(ofRarity.Count)];
            int coins = DuplicateCoins(rarity);
            progress.coins += coins;
            return new ChestResult(duplicate, true, coins);
        }

        /// <summary>Picks a skin from the collection to wear. Returns false for a skin the player does not own.</summary>
        public static bool Equip(PlayerProgress progress, string skinId)
        {
            var skin = SkinCatalog.Find(skinId);
            if (skin == null || !progress.Owns(skinId))
                return false;
            if (skin.Kind == SkinKind.Pieces)
                progress.pieces = skinId;
            else
                progress.board = skinId;
            return true;
        }

        /// <summary>A string hash that does not change between runs, unlike string.GetHashCode.</summary>
        private static int StableHash(string text)
        {
            unchecked
            {
                int hash = 23;
                foreach (char c in text)
                    hash = hash * 31 + c;
                return hash & 0x7fffffff;
            }
        }
    }
}
