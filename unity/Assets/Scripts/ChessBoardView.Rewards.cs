#nullable enable
using System.Collections.Generic;
using System.Linq;
using Chess.Core;
using UnityEngine;

/// <summary>
/// Quests, chests and the skin collection. Progress is saved on this device. Skins are drawn from
/// the ordinary piece images: each colouring maps their light and dark shades onto new colours.
/// </summary>
public partial class ChessBoardView
{
    private enum PieceEffect
    {
        None,
        Rainbow,
        Stars,
        Ghost,
    }

    private readonly struct PieceStyle
    {
        public PieceStyle(string whiteDark, string whiteLight, string blackDark, string blackLight, PieceEffect effect = PieceEffect.None)
        {
            WhiteDark = Hex(whiteDark);
            WhiteLight = Hex(whiteLight);
            BlackDark = Hex(blackDark);
            BlackLight = Hex(blackLight);
            Effect = effect;
        }

        public Color WhiteDark { get; }
        public Color WhiteLight { get; }
        public Color BlackDark { get; }
        public Color BlackLight { get; }
        public PieceEffect Effect { get; }
    }

    private readonly struct BoardStyle
    {
        public BoardStyle(string light, string dark, string frame)
        {
            Light = Hex(light);
            Dark = Hex(dark);
            Frame = Hex(frame);
        }

        public Color Light { get; }
        public Color Dark { get; }
        public Color Frame { get; }
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out var color);
        return color;
    }

    private static readonly Dictionary<string, PieceStyle> PieceStyles = new Dictionary<string, PieceStyle>
    {
        ["ivory"] = new PieceStyle("#6b5a3e", "#fff8e6", "#120c08", "#5a4636"),
        ["mint"] = new PieceStyle("#2e6b5a", "#e6fff4", "#0d2a24", "#3f8f78"),
        ["sand"] = new PieceStyle("#7a5a30", "#fff0cf", "#3a2810", "#a0783e"),
        ["graphite"] = new PieceStyle("#3a3f47", "#e8edf2", "#0b0c0f", "#4a505a"),
        ["ice"] = new PieceStyle("#3d78b8", "#f0fbff", "#071a33", "#3a7fc4"),
        ["emerald"] = new PieceStyle("#1e7a4a", "#d9ffe9", "#032414", "#23a35e"),
        ["ruby"] = new PieceStyle("#a3263a", "#ffe0e6", "#2a0309", "#c72a44"),
        ["copper"] = new PieceStyle("#8a4a24", "#ffd9b8", "#2a1208", "#b8652f"),
        ["gold"] = new PieceStyle("#8a6a10", "#fff3a0", "#1f2228", "#c9ced8"),
        ["neon"] = new PieceStyle("#002a33", "#3ff8ff", "#2a0033", "#ff4ff0"),
        ["lava"] = new PieceStyle("#5a1500", "#ffd04f", "#120300", "#ff5a1f"),
        ["rainbow"] = new PieceStyle("#000000", "#ffffff", "#000000", "#ffffff", PieceEffect.Rainbow),
        ["cosmos"] = new PieceStyle("#2a1a5a", "#d8c8ff", "#05020f", "#6a4ac8", PieceEffect.Stars),
        ["ghost"] = new PieceStyle("#6fa8c8", "#f0ffff", "#0f2a38", "#7fd0e8", PieceEffect.Ghost),
    };

    private static readonly Dictionary<string, BoardStyle> BoardStyles = new Dictionary<string, BoardStyle>
    {
        [SkinCatalog.DefaultBoard] = new BoardStyle("#eed8b6", "#b28460", "#5c3c28"),
        ["tournament"] = new BoardStyle("#eeeed2", "#769656", "#3c4a2c"),
        ["stone"] = new BoardStyle("#d6d6d6", "#8c8c8c", "#3e4046"),
        ["sky"] = new BoardStyle("#e3f1ff", "#7aa7d6", "#2e4a6b"),
        ["marble"] = new BoardStyle("#f2efe9", "#b5aca0", "#6b6258"),
        ["forest"] = new BoardStyle("#dfe8c8", "#4f7a3a", "#2a3a1e"),
        ["chocolate"] = new BoardStyle("#e8c9a0", "#6b3e26", "#3a1e10"),
        ["night"] = new BoardStyle("#5a6a8a", "#1e2742", "#c9a440"),
        ["sakura"] = new BoardStyle("#ffe6ef", "#e88aa8", "#8a3a58"),
        ["desert"] = new BoardStyle("#f7e2b0", "#d39a52", "#7a4a1a"),
        ["neongrid"] = new BoardStyle("#2a3350", "#0b0e17", "#00e5ff"),
        ["royal"] = new BoardStyle("#f3e2a0", "#5a2d82", "#d4af37"),
    };

    private static readonly string[] RewardTabs = { "Задания", "Сундуки", "Коллекция" };
    private static readonly Color RewardsColor = new Color32(214, 150, 40, 255);

    private static Color RarityColor(Rarity rarity) => rarity switch
    {
        Rarity.Common => new Color32(154, 163, 176, 255),
        Rarity.Rare => new Color32(79, 140, 255, 255),
        Rarity.Epic => new Color32(176, 92, 255, 255),
        _ => new Color32(255, 176, 32, 255),
    };

    private static Color ChestColor(ChestType chest) => chest switch
    {
        ChestType.Common => new Color32(150, 104, 62, 255),
        ChestType.Rare => new Color32(64, 120, 220, 255),
        _ => new Color32(150, 72, 220, 255),
    };

    private PlayerProgress _progress = new PlayerProgress();
    private readonly Dictionary<string, Texture2D> _baseTextures = new Dictionary<string, Texture2D>();
    private readonly Dictionary<string, Texture2D> _skinTextures = new Dictionary<string, Texture2D>();
    private readonly SpriteRenderer[] _tiles = new SpriteRenderer[64];
    private SpriteRenderer? _frame;
    private int _rewardTab;
    private Vector2 _rewardScroll;
    private ChestResult? _opened;
    private ChestType _openedChest;
    private float _openedAt;

    // ---------------------------------------------------------------- saving

    private void LoadProgress()
    {
        string json = PlayerPrefs.GetString("progress", "");
        if (json.Length > 0)
        {
            try
            {
                _progress = JsonUtility.FromJson<PlayerProgress>(json) ?? new PlayerProgress();
            }
            catch (System.ArgumentException)
            {
                _progress = new PlayerProgress();
            }
        }
        if (_progress.chests == null || _progress.chests.Length < 3)
            System.Array.Resize(ref _progress.chests, 3);
        _progress.owned ??= new List<string>();
        _progress.modes ??= new List<string>();
        _progress.solved ??= new List<int>();
        _progress.daily ??= new List<QuestState>();
        _progress.achievements ??= new List<string>();
        _progress.totals ??= new int[0];
        if (!_progress.Owns(_progress.pieces))
            _progress.pieces = SkinCatalog.DefaultPieces;
        if (!_progress.Owns(_progress.board))
            _progress.board = SkinCatalog.DefaultBoard;
        Rewards.RefreshDaily(_progress, System.DateTime.Now, _playerId);
        SaveProgress();
    }

    private void SaveProgress()
    {
        PlayerPrefs.SetString("progress", JsonUtility.ToJson(_progress));
        PlayerPrefs.Save();
    }

    private int ClaimableCount()
    {
        Rewards.RefreshDaily(_progress, System.DateTime.Now, _playerId);
        return DailyQuests().Count(q => Rewards.CanClaim(_progress, q)) + Rewards.Achievements.Count(q => Rewards.CanClaim(_progress, q));
    }

    private int ChestCount => _progress.chests.Sum();

    private List<QuestDef> DailyQuests() =>
        _progress.daily.Select(s => Rewards.Quest(s.id)).Where(q => q != null).Select(q => q!).ToList();

    // ---------------------------------------------------------------- game reports

    /// <summary>Counts a finished game for the quests and tells the player when one is done.</summary>
    private void ReportGame(GameReport report)
    {
        int before = ClaimableCount();
        Rewards.Record(_progress, report);
        SaveProgress();
        if (ClaimableCount() > before)
            ShowToast("Задание выполнено! Награда ждёт в «Задания и сундуки».");
    }

    private void ReportBoardGame()
    {
        bool online = _opponent == Opponent.Online;
        bool sameDevice = _opponent == Opponent.SameDevice;
        var line = _game.Active;
        int promotions = 0;
        for (int i = 0; i < line.Moves.Count; i++)
            if (line.Moves[i].Promotion.HasValue && (sameDevice || line.Positions[i].SideToMove == _human))
                promotions++;
        ReportGame(new GameReport
        {
            Mode = _timeTravel ? "timetravel" : _whiteArmy != null ? "army" : "classic",
            Won = PlayerWon,
            Bot = _opponent == Opponent.Bot ? _level : (BotLevel?)null,
            Online = online,
            Captures = _game.CapturedPieces().Count(p => sameDevice || p.Color != _human),
            Promotions = promotions,
        });
    }

    private void ReportFiveDGame()
    {
        bool sameDevice = _opponent == Opponent.SameDevice;
        ReportGame(new GameReport
        {
            Mode = "5d",
            Won = PlayerWon,
            Bot = _opponent == Opponent.Bot ? _level : (BotLevel?)null,
            Online = _opponent == Opponent.Online,
            Captures = _md.CapturedPieces.Count(p => sameDevice || p.Color != _human),
        });
    }

    private void ReportPartyGame(bool? humanWon)
    {
        bool team = _page == Page.Team;
        int captures = team
            ? (PartyHotSeat ? Enumerable.Range(0, TeamGame.Players).Sum(p => _team.CapturedBy(p).Count) : _team.CapturedBy(_teamSeat).Count)
            : (PartyHotSeat ? Enumerable.Range(0, ThreeGame.PlayerCount).Sum(p => _three.CapturedBy(p).Count) : _three.CapturedBy(_threeSeat).Count);
        ReportGame(new GameReport
        {
            Mode = team ? "team" : "three",
            Won = humanWon,
            Online = _partyOnline,
            Captures = captures,
        });
    }

    // ---------------------------------------------------------------- skins

    /// <summary>Puts the chosen colourings on every piece image and board.</summary>
    private void ApplySkins()
    {
        foreach (var key in _baseTextures.Keys.ToList())
        {
            var texture = SkinTexture(_progress.pieces, key);
            _pieceTextures[key] = texture;
            _pieceSprites[key] = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
        }

        var board = BoardStyles.TryGetValue(_progress.board, out var style) ? style : BoardStyles[SkinCatalog.DefaultBoard];
        LightSquare = board.Light;
        DarkSquare = board.Dark;
        _checker = null;
        for (int i = 0; i < 64; i++)
        {
            if (_tiles[i] != null)
            {
                var square = new Square(i);
                _tiles[i].color = (square.File + square.Rank) % 2 == 0 ? DarkSquare : LightSquare;
            }
        }
        if (_frame != null)
            _frame.color = board.Frame;
        RedrawPieces();
    }

    /// <summary>The piece image <paramref name="key"/> ("wQ") in a colouring, made once and kept.</summary>
    private Texture2D SkinTexture(string skin, string key)
    {
        if (!_baseTextures.TryGetValue(key, out var source))
            return Texture2D.whiteTexture;
        if (!PieceStyles.TryGetValue(skin, out var style))
            return source;
        string cacheKey = skin + "/" + key;
        if (_skinTextures.TryGetValue(cacheKey, out var cached))
            return cached;

        var texture = Readable(source);
        var pixels = texture.GetPixels();
        int width = texture.width, height = texture.height;
        bool white = key[0] == 'w';
        var dark = white ? style.WhiteDark : style.BlackDark;
        var light = white ? style.WhiteLight : style.BlackLight;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                var c = pixels[i];
                if (c.a <= 0.01f)
                    continue;
                float shade = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                Color result;
                switch (style.Effect)
                {
                    case PieceEffect.Rainbow:
                        float hue = Mathf.Repeat((float)y / height * 0.9f + (float)x / width * 0.25f + (white ? 0 : 0.5f), 1);
                        result = Color.HSVToRGB(hue, white ? 0.55f : 0.85f, Mathf.Lerp(white ? 0.35f : 0.12f, 1, shade));
                        break;
                    case PieceEffect.Stars:
                        result = Color.Lerp(dark, light, shade);
                        // A few fixed sparkles on the body of the piece.
                        uint hash = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)key.GetHashCode();
                        if (shade > 0.15f && hash % 1000 < 6)
                            result = Color.white;
                        break;
                    default:
                        result = Color.Lerp(dark, light, shade);
                        break;
                }
                result.a = style.Effect == PieceEffect.Ghost ? c.a * 0.72f : c.a;
                pixels[i] = result;
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        texture.filterMode = FilterMode.Bilinear;
        _skinTextures[cacheKey] = texture;
        return texture;
    }

    /// <summary>A copy of a texture whose pixels can be read, even when the original cannot.</summary>
    private static Texture2D Readable(Texture2D source)
    {
        var target = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(source, target);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        copy.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(target);
        return copy;
    }

    // ---------------------------------------------------------------- screen

    private void DrawRewards(float width, float height)
    {
        bool window = _opened != null;
        Theme.InputBlocked = window;
        BackButton();

        GUI.Label(new Rect(width - 260, 16, 244, 46), $"Монеты: {_progress.coins}", Theme.Label(22, Theme.Gold, TextAnchor.MiddleRight, bold: true));

        float cardWidth = Mathf.Min(760, width - 24);
        float x = (width - cardWidth) / 2;
        float y = 76;
        var card = new Rect(x, y, cardWidth, height - y - 16);
        Theme.PanelBox(card, 24);

        int claim = ClaimableCount();
        var tabs = (string[])RewardTabs.Clone();
        if (claim > 0)
            tabs[0] += $" ({claim})";
        if (ChestCount > 0)
            tabs[1] += $" ({ChestCount})";
        int tab = Theme.Pills(new Rect(x + 20, y + 16, cardWidth - 40, 46), _rewardTab, tabs, RewardsColor, cardWidth < 520 ? 14 : 17);
        if (tab != _rewardTab)
        {
            _rewardTab = tab;
            _rewardScroll = Vector2.zero;
        }

        var view = new Rect(x + 16, y + 76, cardWidth - 32, card.height - 92);
        float inner = view.width - 20;
        float contentHeight = _rewardTab == 0 ? QuestsHeight() : _rewardTab == 1 ? ChestsHeight(inner) : CollectionHeight(inner);
        _rewardScroll = GUI.BeginScrollView(view, _rewardScroll, new Rect(0, 0, inner, contentHeight));
        if (_rewardTab == 0)
            DrawQuests(inner);
        else if (_rewardTab == 1)
            DrawChests(inner);
        else
            DrawCollection(inner);
        GUI.EndScrollView();

        Theme.InputBlocked = false;
        if (_opened != null)
            DrawOpenedChest(width, height);
    }

    // Quests.

    private const float QuestRow = 78;

    private float QuestsHeight() => 40 + _progress.daily.Count * QuestRow + 50 + Rewards.Achievements.Count * QuestRow + 10;

    private void DrawQuests(float width)
    {
        float y = 0;
        GUI.Label(new Rect(4, y, width, 32), "Задания на сегодня", Theme.Label(20, Theme.Text, bold: true));
        GUI.Label(new Rect(4, y, width - 4, 32), "новые каждый день", Theme.Label(14, Theme.Muted, TextAnchor.MiddleRight));
        y += 40;
        foreach (var quest in DailyQuests())
        {
            DrawQuest(new Rect(0, y, width, QuestRow - 8), quest);
            y += QuestRow;
        }
        y += 10;
        GUI.Label(new Rect(4, y, width, 32), "Достижения", Theme.Label(20, Theme.Text, bold: true));
        y += 40;
        foreach (var quest in Rewards.Achievements)
        {
            DrawQuest(new Rect(0, y, width, QuestRow - 8), quest);
            y += QuestRow;
        }
    }

    private void DrawQuest(Rect row, QuestDef quest)
    {
        bool claimed = Rewards.IsClaimed(_progress, quest);
        bool ready = Rewards.CanClaim(_progress, quest);
        int done = Rewards.Progress(_progress, quest);
        Theme.Fill(row, ready ? Theme.WithAlpha(Theme.Gold, 0.2f) : Theme.WithAlpha(Theme.PanelLight, claimed ? 0.3f : 0.6f), 14);
        if (ready)
            Theme.Outline(row, Theme.Gold, 2, 14);

        float buttonWidth = 130;
        float textWidth = row.width - buttonWidth - 36;
        GUI.Label(new Rect(row.x + 14, row.y + 6, textWidth, 26), quest.Text, Theme.Label(17, claimed ? Theme.Muted : Theme.Text, bold: true));
        GUI.Label(new Rect(row.x + 14, row.y + 30, textWidth, 20), "Награда: " + quest.Reward, Theme.Label(14, Theme.Gold));
        Theme.Bar(new Rect(row.x + 14, row.y + 52, textWidth - 60, 14), (float)done / quest.Target, ready || claimed ? Theme.Green : RewardsColor);
        GUI.Label(new Rect(row.x + textWidth - 38, row.y + 48, 56, 22), $"{done}/{quest.Target}", Theme.Label(14, Theme.Muted, TextAnchor.MiddleRight));

        var button = new Rect(row.xMax - buttonWidth - 12, row.y + (row.height - 44) / 2, buttonWidth, 44);
        if (claimed)
        {
            GUI.Label(button, "Получено", Theme.Label(16, Theme.Green, TextAnchor.MiddleCenter, bold: true));
        }
        else if (Theme.Button(button, "Забрать", Theme.Green, 17, ready))
        {
            Rewards.Claim(_progress, quest);
            SaveProgress();
            Sfx.Play(Sfx.Win);
            ShowToast("Получено: " + quest.Reward + ".");
        }
    }

    // Chests.

    private float ChestsHeight(float width) => (width >= 560 ? 1 : 3) * 290 + 90;

    private void DrawChests(float width)
    {
        bool row = width >= 560;
        float cardWidth = row ? (width - 24) / 3 : width;
        for (int i = 0; i < 3; i++)
        {
            var chest = (ChestType)i;
            var rect = row ? new Rect(i * (cardWidth + 12), 0, cardWidth, 280) : new Rect(0, i * 290, cardWidth, 280);
            DrawChestCard(rect, chest);
        }
        float y = row ? 296 : 3 * 290 + 6;
        int left = Rewards.Pity - _progress.sinceLegendary;
        GUI.Label(new Rect(4, y, width - 8, 70),
            $"Легендарная раскраска гарантирована не позже чем через {left} {Plural(left, "сундук", "сундука", "сундуков")}. " +
            "Сундуки дают за задания и достижения, их можно купить за монеты. Повторная раскраска превращается в монеты.",
            Theme.Label(14, Theme.Muted, TextAnchor.UpperLeft, wrap: true));
    }

    private void DrawChestCard(Rect rect, ChestType chest)
    {
        var color = ChestColor(chest);
        Theme.Fill(rect, Theme.WithAlpha(color, 0.18f), 16);
        Theme.Outline(rect, Theme.WithAlpha(color, 0.7f), 2, 16);
        GUI.Label(new Rect(rect.x, rect.y + 8, rect.width, 28), SkinCatalog.ChestName(chest), Theme.Label(18, Theme.Text, TextAnchor.MiddleCenter, bold: true));
        DrawChestIcon(new Rect(rect.center.x - 46, rect.y + 42, 92, 70), color, Mathf.Sin(Time.time * 2 + (int)chest) * 2);
        int have = _progress.chests[(int)chest];
        GUI.Label(new Rect(rect.x, rect.y + 116, rect.width, 24), have > 0 ? $"У вас: {have}" : "Нет сундуков", Theme.Label(16, have > 0 ? Theme.Gold : Theme.Muted, TextAnchor.MiddleCenter, bold: true));

        var odds = Rewards.Odds(chest);
        string oddsText = string.Join("  ", Enumerable.Range(0, 4).Where(r => odds[r] > 0).Select(r => $"<color=#{ColorUtility.ToHtmlStringRGB(RarityColor((Rarity)r))}>{odds[r]}%</color>"));
        var oddsStyle = Theme.Label(14, Theme.Text, TextAnchor.MiddleCenter);
        oddsStyle.richText = true;
        GUI.Label(new Rect(rect.x, rect.y + 140, rect.width, 22), oddsText, oddsStyle);

        if (Theme.Button(new Rect(rect.x + 12, rect.y + 170, rect.width - 24, 46), "Открыть", Theme.Green, 18, have > 0))
        {
            _opened = Rewards.Open(_progress, chest, new System.Random());
            _openedChest = chest;
            _openedAt = Time.time;
            SaveProgress();
            Sfx.Play(Sfx.Jump);
        }
        int price = Rewards.Price(chest);
        if (Theme.Button(new Rect(rect.x + 12, rect.y + 224, rect.width - 24, 42), $"Купить за {price}", Theme.Blue, 15, _progress.coins >= price))
        {
            Rewards.Buy(_progress, chest);
            SaveProgress();
            Sfx.Play(Sfx.Click);
        }
    }

    private static void DrawChestIcon(Rect rect, Color color, float lift)
    {
        var body = new Rect(rect.x, rect.y + rect.height * 0.4f, rect.width, rect.height * 0.6f);
        var lid = new Rect(rect.x - 3, rect.y + rect.height * 0.08f - lift, rect.width + 6, rect.height * 0.36f);
        Theme.Fill(new Rect(body.x + 3, body.y + 5, body.width, body.height), new Color(0, 0, 0, 0.35f), 8);
        Theme.Gradient(body, Theme.Lighten(color, 0.15f), Theme.Darken(color, 0.25f), 8);
        Theme.Gradient(lid, Theme.Lighten(color, 0.3f), color, 10);
        var band = Theme.Lighten(Theme.Gold, 0.1f);
        Theme.Fill(new Rect(body.center.x - 6, body.y, 12, body.height), band, 3);
        Theme.Fill(new Rect(lid.center.x - 6, lid.y, 12, lid.height), band, 3);
        Theme.Fill(new Rect(body.center.x - 10, body.y + 6, 20, 18), Theme.Gold, 5);
    }

    private void DrawOpenedChest(float width, float height)
    {
        Dim(width, height, 0.75f);
        var opened = _opened!;
        float age = Time.time - _openedAt;
        float cardWidth = Mathf.Min(460, width - 24);
        var card = new Rect((width - cardWidth) / 2, (height - 420) / 2, cardWidth, 420);
        var rarityColor = RarityColor(opened.Skin.Rarity);

        if (age < 0.9f)
        {
            // The chest shakes harder until it opens.
            float shake = Mathf.Sin(age * 60) * age * 10;
            DrawChestIcon(new Rect(width / 2 - 70 + shake, height / 2 - 60, 140, 110), ChestColor(_openedChest), age * 12);
            return;
        }
        float appear = Mathf.Clamp01((age - 0.9f) / 0.3f);
        GUI.color = new Color(1, 1, 1, appear);
        Theme.Card(card, Theme.Darken(rarityColor, 0.55f), 26);
        // Rays behind the prize.
        float glow = 150 + Mathf.Sin(Time.time * 3) * 8;
        Theme.Fill(new Rect(card.center.x - glow / 2, card.y + 70, glow, glow), Theme.WithAlpha(rarityColor, 0.35f), glow / 2);

        GUI.Label(new Rect(card.x, card.y + 18, card.width, 34), SkinCatalog.RarityName(opened.Skin.Rarity).ToUpper(),
            Theme.Label(22, rarityColor, TextAnchor.MiddleCenter, display: true));
        DrawSkinPreview(new Rect(card.center.x - 70, card.y + 75, 140, 140), opened.Skin);
        GUI.Label(new Rect(card.x, card.y + 228, card.width, 36), opened.Skin.Name, Theme.Label(26, Theme.Text, TextAnchor.MiddleCenter, bold: true));
        string kind = opened.Skin.Kind == SkinKind.Pieces ? "раскраска фигур" : "раскраска доски";
        string note = opened.Duplicate ? $"Уже есть — превратилась в {opened.Coins} монет" : "Новая " + kind + "!";
        GUI.Label(new Rect(card.x + 20, card.y + 266, card.width - 40, 28), note, Theme.Label(17, opened.Duplicate ? Theme.Muted : Theme.Gold, TextAnchor.MiddleCenter));

        float buttonWidth = (cardWidth - 60) / 2;
        if (!opened.Duplicate && Theme.Button(new Rect(card.x + 20, card.yMax - 84, buttonWidth, 54), "Надеть", rarityColor, 19))
        {
            Rewards.Equip(_progress, opened.Skin.Id);
            SaveProgress();
            ApplySkins();
            _opened = null;
        }
        var okRect = opened.Duplicate ? new Rect(card.x + (cardWidth - buttonWidth) / 2, card.yMax - 84, buttonWidth, 54) : new Rect(card.x + 40 + buttonWidth, card.yMax - 84, buttonWidth, 54);
        if (_opened != null && Theme.Button(okRect, "Отлично", Theme.Green, 19))
            _opened = null;
        GUI.color = Color.white;
    }

    // Collection.

    private const float SkinCell = 150;

    private float CollectionHeight(float width)
    {
        int columns = Mathf.Max(2, (int)(width / SkinCell));
        int pieceRows = (SkinCatalog.All.Count(s => s.Kind == SkinKind.Pieces) + columns - 1) / columns;
        int boardRows = (SkinCatalog.All.Count(s => s.Kind == SkinKind.Board) + columns - 1) / columns;
        return 40 + pieceRows * (SkinCell + 40) + 50 + boardRows * (SkinCell + 40);
    }

    private void DrawCollection(float width)
    {
        int columns = Mathf.Max(2, (int)(width / SkinCell));
        float cell = width / columns;
        float y = 0;
        foreach (var kind in new[] { SkinKind.Pieces, SkinKind.Board })
        {
            var skins = SkinCatalog.All.Where(s => s.Kind == kind).ToList();
            int owned = skins.Count(s => _progress.Owns(s.Id));
            GUI.Label(new Rect(4, y, width, 32), kind == SkinKind.Pieces ? "Фигуры" : "Доски", Theme.Label(20, Theme.Text, bold: true));
            GUI.Label(new Rect(4, y, width - 4, 32), $"собрано {owned} из {skins.Count}", Theme.Label(14, Theme.Muted, TextAnchor.MiddleRight));
            y += 40;
            for (int i = 0; i < skins.Count; i++)
            {
                var rect = new Rect(i % columns * cell + 4, y + i / columns * (SkinCell + 40), cell - 8, SkinCell + 32);
                DrawSkinCard(rect, skins[i]);
            }
            y += (skins.Count + columns - 1) / columns * (SkinCell + 40) + 10;
        }
    }

    private void DrawSkinCard(Rect rect, Skin skin)
    {
        bool owned = _progress.Owns(skin.Id);
        bool worn = skin.Id == (skin.Kind == SkinKind.Pieces ? _progress.pieces : _progress.board);
        var color = RarityColor(skin.Rarity);
        Theme.Fill(rect, Theme.WithAlpha(color, owned ? 0.2f : 0.08f), 14);
        Theme.Outline(rect, worn ? Theme.Gold : Theme.WithAlpha(color, owned ? 0.8f : 0.3f), worn ? 3 : 2, 14);

        var preview = new Rect(rect.center.x - 50, rect.y + 10, 100, 100);
        if (owned)
        {
            DrawSkinPreview(preview, skin);
        }
        else
        {
            GUI.color = new Color(1, 1, 1, 0.25f);
            DrawSkinPreview(preview, skin);
            GUI.color = Color.white;
            GUI.Label(preview, "?", Theme.Label(48, Theme.Text, TextAnchor.MiddleCenter, display: true));
        }
        GUI.Label(new Rect(rect.x + 4, rect.y + 112, rect.width - 8, 24), skin.Name, Theme.Label(15, owned ? Theme.Text : Theme.Muted, TextAnchor.MiddleCenter, bold: true));
        string status = worn ? "выбрано" : owned ? "нажми, чтобы надеть" : SkinCatalog.RarityName(skin.Rarity).ToLower();
        GUI.Label(new Rect(rect.x + 4, rect.y + 136, rect.width - 8, 20), status, Theme.Label(13, worn ? Theme.Gold : color, TextAnchor.MiddleCenter));

        if (owned && !worn && !Theme.InputBlocked && GUI.Button(rect, GUIContent.none, GUIStyle.none))
        {
            Rewards.Equip(_progress, skin.Id);
            SaveProgress();
            ApplySkins();
            Sfx.Play(Sfx.Click);
        }
    }

    /// <summary>A small picture of a skin: a white and a black queen, or a corner of the board.</summary>
    private void DrawSkinPreview(Rect rect, Skin skin)
    {
        if (skin.Kind == SkinKind.Pieces)
        {
            var white = SkinTexture(skin.Id, "wQ");
            var black = SkinTexture(skin.Id, "bQ");
            GUI.DrawTexture(new Rect(rect.x - rect.width * 0.08f, rect.y + rect.height * 0.12f, rect.width * 0.7f, rect.height * 0.7f), white);
            GUI.DrawTexture(new Rect(rect.x + rect.width * 0.38f, rect.y + rect.height * 0.22f, rect.width * 0.7f, rect.height * 0.7f), black);
            return;
        }
        var style = BoardStyles.TryGetValue(skin.Id, out var s) ? s : BoardStyles[SkinCatalog.DefaultBoard];
        var tint = GUI.color;
        Theme.Fill(rect, style.Frame * tint, 10);
        float inset = rect.width * 0.08f;
        float cell = (rect.width - 2 * inset) / 4;
        for (int file = 0; file < 4; file++)
            for (int rank = 0; rank < 4; rank++)
                Theme.Fill(new Rect(rect.x + inset + file * cell, rect.y + inset + rank * cell, cell, cell), ((file + rank) % 2 == 0 ? style.Light : style.Dark) * tint);
        var knight = PieceTexture(PieceType.Knight, PieceColor.White);
        if (knight != null)
            GUI.DrawTexture(new Rect(rect.x + inset + cell, rect.y + inset + cell, cell * 2, cell * 2), knight);
    }

    private static string Plural(int n, string one, string few, string many)
    {
        int mod100 = n % 100, mod10 = n % 10;
        if (mod100 >= 11 && mod100 <= 14)
            return many;
        return mod10 == 1 ? one : mod10 >= 2 && mod10 <= 4 ? few : many;
    }
}
