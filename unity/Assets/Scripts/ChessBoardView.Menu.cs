#nullable enable
using System.Collections.Generic;
using Chess.Core;
using UnityEngine;

/// <summary>The menu screens: the home screen with mode tiles, game setup and the army editor.</summary>
public partial class ChessBoardView
{
    private enum Page
    {
        Home,
        BotSetup,
        TimeSetup,
        Army,
        Online,
        Game,
        FiveDSetup,
        FiveD,
        TeamSetup,
        Team,
        ThreeSetup,
        Three,
        Rewards,
        Puzzles,
        Puzzle,
    }

    private static readonly string[] LevelNames = { "Лёгкий", "Средний", "Сложный" };
    private static readonly string[] LevelHints =
    {
        "Иногда ошибается. Хорош для разминки.",
        "Думает на два хода вперёд.",
        "Думает на три хода и не прощает ошибок.",
    };
    private static readonly string[] ColorNames = { "Белые", "Чёрные", "Случайно" };
    private static readonly string[] OpponentNames = { "Бот", "Вдвоём" };

    private static readonly PieceType[] ArmyTools =
    {
        PieceType.King, PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight, PieceType.Pawn,
    };

    private Page _page = Page.Home;
    private float _pageShownAt;
    private int _timeOpponent;

    private string _toast = "";
    private float _toastAt = -100;

    private readonly Dictionary<Square, PieceType> _editArmy = new Dictionary<Square, PieceType>();
    private PieceType? _armyTool = PieceType.Queen;
    private Vector2 _homeScroll;

    private void ShowPage(Page page)
    {
        if (page != Page.Game)
        {
            CancelBot();
            _root.gameObject.SetActive(false);
        }
        if (page != Page.FiveD)
            CancelFiveDBot();
        if (page != Page.Team && page != Page.Three)
            CancelPartyBots();
        _page = page;
        _pageShownAt = Time.time;
        if (page == Page.Online)
            RefreshOnlineInfo();
    }

    private void ShowToast(string text)
    {
        _toast = text;
        _toastAt = Time.time;
    }

    private void LoadMenuSettings()
    {
        _level = (BotLevel)Mathf.Clamp(PlayerPrefs.GetInt("level", 1), 0, 2);
        _menuColor = Mathf.Clamp(PlayerPrefs.GetInt("color", 0), 0, 2);
        _playerName = PlayerPrefs.GetString("name", "Игрок " + Random.Range(100, 1000));
        _serverUrl = PlayerPrefs.GetString("server", DefaultServer);
        // The server keeps the rating under this id, so it stays with this copy of the game.
        _playerId = PlayerPrefs.GetString("playerId", "");
        if (_playerId.Length == 0)
        {
            _playerId = System.Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString("playerId", _playerId);
        }
        _playerSecret = PlayerPrefs.GetString("playerSecret", "");
        if (_playerSecret.Length < 16)
        {
            _playerSecret = System.Guid.NewGuid().ToString("N") + System.Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString("playerSecret", _playerSecret);
        }
        var army = Army.FromCode(PlayerPrefs.GetString("army", ""));
        SetEditArmy(army != null && army.Pieces.Count > 0 ? army : Army.Classic);
    }

    private void SaveMenuSettings()
    {
        PlayerPrefs.SetInt("level", (int)_level);
        PlayerPrefs.SetInt("color", _menuColor);
        PlayerPrefs.SetString("name", _playerName);
        PlayerPrefs.SetString("server", _serverUrl);
        PlayerPrefs.SetString("army", PlayerArmy().ToCode());
        PlayerPrefs.Save();
    }

    private void SetEditArmy(Army army)
    {
        _editArmy.Clear();
        foreach (var pair in army.Pieces)
            _editArmy[pair.Key] = pair.Value;
    }

    private Army PlayerArmy() => new Army(_editArmy);

    // ---------------------------------------------------------------- OnGUI

    private void OnGUI()
    {
        Theme.Init();
        // Layout is written for a 720 px tall screen (at least 420 px wide) and scaled to the real one.
        float scale = Mathf.Min(Screen.height / 720f, Screen.width / 420f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float width = Screen.width / scale;
        float height = Screen.height / scale;
        CelebrationInput(width, height);

        if (_page == Page.Game)
        {
            DrawGameUi(width, height, scale);
        }
        else if (_page == Page.FiveD)
        {
            DrawFiveD(width, height);
        }
        else if (_page == Page.Team)
        {
            DrawTeam(width, height);
        }
        else if (_page == Page.Three)
        {
            DrawThree(width, height);
        }
        else if (_page == Page.Puzzle)
        {
            DrawPuzzle(width, height);
        }
        else
        {
            Theme.Background(width, height, _floatingPieces);
            // New screens fade in.
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01((Time.time - _pageShownAt) / 0.25f));
            switch (_page)
            {
                case Page.Home:
                    DrawHome(width, height);
                    break;
                case Page.BotSetup:
                case Page.TimeSetup:
                    DrawSetup(width, height);
                    break;
                case Page.Army:
                    DrawArmyEditor(width, height);
                    break;
                case Page.Online:
                    DrawOnline(width, height);
                    break;
                case Page.FiveDSetup:
                    DrawFiveDSetup(width, height);
                    break;
                case Page.TeamSetup:
                    DrawPartySetup(width, height, false);
                    break;
                case Page.ThreeSetup:
                    DrawPartySetup(width, height, true);
                    break;
                case Page.Rewards:
                    DrawRewards(width, height);
                    break;
                case Page.Puzzles:
                    DrawPuzzleList(width, height);
                    break;
            }
            GUI.color = Color.white;
        }

        DrawToast(width, height);
        DrawCoins(width, height);
        DrawCelebration(width, height);
    }

    private void DrawToast(float width, float height)
    {
        float age = Time.time - _toastAt;
        if (age > 5 || _toast.Length == 0)
            return;
        GUI.color = new Color(1, 1, 1, Mathf.Clamp01((5 - age) * 2));
        var style = Theme.Label(17, Theme.Text, TextAnchor.MiddleCenter, wrap: true);
        float boxWidth = Mathf.Min(560, width - 32);
        float boxHeight = style.CalcHeight(new GUIContent(_toast), boxWidth - 32) + 24;
        var rect = new Rect((width - boxWidth) / 2, height - boxHeight - 30, boxWidth, boxHeight);
        Theme.Card(rect, Theme.Darken(Theme.Red, 0.25f), 16);
        GUI.Label(new Rect(rect.x + 16, rect.y, rect.width - 32, rect.height), _toast, style);
        GUI.color = Color.white;
    }

    private bool BackButton()
    {
        if (Theme.Button(new Rect(16, 16, 130, 46), "‹ Назад", Theme.PanelLight, 19))
        {
            SaveMenuSettings();
            ShowPage(Page.Home);
            return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- home

    private void DrawHome(float width, float height)
    {
        float time = Time.time;
        var title = Theme.Label(width < 520 ? 52 : 68, Theme.Gold, TextAnchor.MiddleCenter, display: true);
        float bob = Mathf.Sin(time * 1.6f) * 3;
        Theme.ShadowText(new Rect(0, 34 + bob, width, 80), "ШАХМАТЫ", title, 4);
        GUI.Label(new Rect(0, 108, width, 30), "время  •  армии  •  онлайн",
            Theme.Label(19, Theme.Muted, TextAnchor.MiddleCenter, bold: true));

        if (Theme.Button(new Rect(width - 150, 16, 134, 40), Sfx.Enabled ? "Звук: вкл" : "Звук: выкл", Theme.PanelLight, 16))
            Sfx.Enabled = !Sfx.Enabled;

        int columns = width >= 780 ? 2 : 1;
        int count = 10;
        int rows = (count + columns - 1) / columns;
        // Five rows of tiles fit on the screen when they are a little lower.
        float gap = rows >= 5 ? 14 : 18;
        float tileWidth = columns == 2 ? Mathf.Min(380, (width - 48 - gap) / 2) : Mathf.Min(480, width - 32);
        float tileHeight = rows >= 5 ? 96 : 112;
        float gridWidth = columns * tileWidth + (columns - 1) * gap;
        float top = rows >= 5 ? 146 : 156;
        float gridHeight = rows * (tileHeight + gap) + 20;

        var view = new Rect(0, top, width, height - top);
        var content = new Rect(0, 0, width - 20, gridHeight);
        bool scroll = gridHeight > view.height;
        if (scroll)
        {
            _homeScroll = Theme.DragScroll(view, _homeScroll);
            _homeScroll = GUI.BeginScrollView(view, _homeScroll, content, false, false, GUIStyle.none, GUIStyle.none);
        }
        else
            GUI.BeginGroup(view);

        float left = (width - gridWidth) / 2;
        Rect Cell(int index) => new Rect(left + index % columns * (tileWidth + gap), 6 + index / columns * (tileHeight + gap), tileWidth, tileHeight);

        if (Tile(Cell(0), "Против бота", "Три уровня сложности. Бот думает, а не ходит мгновенно.", Theme.Green, "wN", 0))
            ShowPage(Page.BotSetup);
        if (Tile(Cell(1), "Вдвоём", "Играйте по очереди на одном устройстве.", Theme.Blue, "wK", 1))
            StartSameDeviceGame(false);
        if (Tile(Cell(2), "Путешествие во времени", "Отправь фигуру в прошлое и открой новую линию времени.", Theme.Purple, "wQ", 2))
            ShowPage(Page.TimeSetup);
        if (Tile(Cell(3), "5D шахматы", "Все доски сразу: ходи в прошлое и между линиями времени.", FiveDColor, "wB", 3))
            ShowPage(Page.FiveDSetup);
        if (Tile(Cell(4), "Своя армия", "Собери войско на 39 очков. Хоть четыре ферзя!", Theme.Orange, "wR", 4))
            ShowPage(Page.Army);
        if (Tile(Cell(5), "Союзники: 2 на 2", "Команда из двух игроков: один ведёт ферзя, другой — короля.", TeamColor, "wP", 5))
            ShowPage(Page.TeamSetup);
        if (Tile(Cell(6), "Тройной трон", "Трое на доске 12×12. Съел короля — забрал его армию.", ThreeColor, "bK", 6))
            ShowPage(Page.ThreeSetup);
        if (Tile(Cell(7), "Онлайн", "Найди соперника или позови друга по коду комнаты.", Theme.Teal, "bN", 7))
            ShowPage(Page.Online);
        int waiting = ClaimableCount() + ChestCount;
        if (Tile(Cell(8), "Задания и сундуки", "Выполняй задания, открывай сундуки и собирай раскраски.", RewardsColor, "bQ", 8, badge: waiting > 0 ? waiting.ToString() : null))
            ShowPage(Page.Rewards);
        if (Tile(Cell(9), "Задачи", "Мат в 1 и в 2 хода. За каждую решённую — монеты.", PuzzleColor, "wK", 9))
            ShowPage(Page.Puzzles);

        if (scroll)
            GUI.EndScrollView();
        else
            GUI.EndGroup();
    }

    private bool Tile(Rect rect, string title, string text, Color color, string icon, int index, bool locked = false, string? badge = null)
    {
        bool hover = rect.Contains(Event.current.mousePosition);
        var face = rect;
        if (hover)
            face.y -= 4;
        Theme.Card(face, hover ? Theme.Lighten(color, 0.1f) : color, 20);

        // The icon sits in a dark circle and bobs gently, faster under the pointer.
        var circle = new Rect(face.x + 14, face.y + (face.height - 84) / 2, 84, 84);
        Theme.Fill(circle, new Color(0, 0, 0, 0.22f), 42);
        float bob = Mathf.Sin(Time.time * (hover ? 6 : 2) + index) * (hover ? 4 : 2);
        if (_pieceTextures.TryGetValue(icon, out var texture))
            GUI.DrawTexture(new Rect(circle.x + 6, circle.y + 4 + bob, 72, 72), texture);

        float textLeft = circle.xMax + 14;
        float textWidth = face.xMax - textLeft - 14;
        GUI.Label(new Rect(textLeft, face.y + 14, textWidth, 30), title, Theme.Label(title.Length > 16 ? 21 : 24, Theme.Text, bold: true));
        GUI.Label(new Rect(textLeft, face.y + 46, textWidth, face.height - 52), text,
            Theme.Label(15, Theme.WithAlpha(Theme.Text, 0.85f), TextAnchor.UpperLeft, wrap: true));

        if (locked)
        {
            var soon = new Rect(face.xMax - 86, face.y - 10, 78, 26);
            Theme.Card(soon, Theme.Gold, 13, raised: false);
            GUI.Label(soon, "СКОРО", Theme.Label(13, new Color32(60, 40, 0, 255), TextAnchor.MiddleCenter, bold: true));
        }
        else if (badge != null)
        {
            // How many rewards are waiting.
            var dot = new Rect(face.xMax - 40, face.y - 10, 34, 34);
            Theme.Card(dot, Theme.Red, 17, raised: false);
            GUI.Label(dot, badge, Theme.Label(16, Theme.Text, TextAnchor.MiddleCenter, bold: true));
        }

        bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
        if (clicked)
            Sfx.Play(Sfx.Click);
        return clicked;
    }

    // ---------------------------------------------------------------- bot and time travel setup

    private void DrawSetup(float width, float height)
    {
        BackButton();
        bool time = _page == Page.TimeSetup;
        bool bot = !time || _timeOpponent == 0;
        var accent = time ? Theme.Purple : Theme.Green;

        float cardWidth = Mathf.Min(560, width - 32);
        float x = (width - cardWidth) / 2;
        float cardHeight = (bot ? 330 : 120) + (time ? 250 : 0);
        float y = Mathf.Max(74, (height - cardHeight) / 2);
        var card = new Rect(x, y, cardWidth, cardHeight);
        Theme.PanelBox(card, 24);

        float inner = cardWidth - 48;
        float cx = x + 24;
        float cy = y + 20;

        if (_pieceTextures.TryGetValue(time ? "wQ" : "wN", out var icon))
            GUI.DrawTexture(new Rect(cx, cy - 4, 56, 56), icon);
        GUI.Label(new Rect(cx + 66, cy, inner - 66, 48), time ? "Путешествие во времени" : "Против бота",
            Theme.Label(cardWidth < 480 ? 24 : 30, Theme.Text, display: true));
        cy += 66;

        if (time)
        {
            GUI.Label(new Rect(cx, cy, inner, 24), "Соперник", Theme.Label(16, Theme.Muted, bold: true));
            _timeOpponent = Theme.Pills(new Rect(cx, cy + 26, inner, 44), _timeOpponent, OpponentNames, accent);
            cy += 84;
        }

        if (bot)
        {
            GUI.Label(new Rect(cx, cy, inner, 24), "Ваш цвет", Theme.Label(16, Theme.Muted, bold: true));
            _menuColor = Theme.Pills(new Rect(cx, cy + 26, inner, 44), _menuColor, ColorNames, accent);
            cy += 84;
            GUI.Label(new Rect(cx, cy, inner, 24), "Сложность", Theme.Label(16, Theme.Muted, bold: true));
            _level = (BotLevel)Theme.Pills(new Rect(cx, cy + 26, inner, 44), (int)_level, LevelNames, accent);
            GUI.Label(new Rect(cx, cy + 74, inner, 24), LevelHints[(int)_level], Theme.Label(15, Theme.Muted));
            cy += 112;
        }

        if (time)
        {
            var rules = new Rect(cx, cy, inner, 150);
            Theme.Fill(rules, Theme.WithAlpha(Theme.Purple, 0.18f), 14);
            Theme.Outline(rules, Theme.WithAlpha(Theme.Purple, 0.6f), 2, 14);
            string text =
                "•  Вместо хода можно отправить фигуру (кроме короля) на доску, какой она была 1–3 ваших хода назад.\n" +
                "•  С этой доски начинается новая линия времени.\n" +
                $"•  У каждого {TimeTravelGame.JumpsPerPlayer} прыжка, между ними не меньше {TimeTravelGame.Cooldown} своих ходов.\n" +
                "•  Нельзя прыгать на занятую клетку и появляться с шахом.";
            GUI.Label(new Rect(rules.x + 14, rules.y + 8, rules.width - 28, rules.height - 16), text,
                Theme.Label(15, Theme.Text, TextAnchor.MiddleLeft, wrap: true));
            cy += 166;
        }

        if (Theme.Button(new Rect(cx, cy, inner, 62), "ИГРАТЬ", Theme.Green, 26))
        {
            SaveMenuSettings();
            if (time && _timeOpponent == 1)
                StartSameDeviceGame(true);
            else
                StartBotGame(time, null);
        }
    }

    // ---------------------------------------------------------------- army editor

    private static string ArmyErrorText(ArmyError error) => error switch
    {
        ArmyError.KingCount => "Нужен ровно один король.",
        ArmyError.OverBudget => $"Слишком дорого: бюджет {Army.Budget} очков.",
        ArmyError.TooManyPieces => $"Не больше {Army.MaxPieces} фигур.",
        ArmyError.OutsideHomeRanks => "Фигуры стоят только в первых двух рядах.",
        _ => "Пешки не могут стоять в первом ряду.",
    };

    private void DrawArmyEditor(float width, float height)
    {
        BackButton();
        float cardWidth = Mathf.Min(700, width - 24);
        float x = (width - cardWidth) / 2;
        float inner = cardWidth - 40;
        float cell = Mathf.Min(64, inner / 8);
        float tool = Mathf.Min(80, inner / 7 - 6);
        bool wide = cardWidth >= 600;
        float cardHeight = 116 + cell * 2 + 30 + tool + 22 + 76 + (wide ? 56 : 104) + 76;
        float y = Mathf.Max(70, (height - cardHeight) / 2);
        Theme.PanelBox(new Rect(x, y, cardWidth, cardHeight), 24);
        float cx = x + 20;
        float cy = y + 16;

        GUI.Label(new Rect(cx, cy, inner, 40), "Своя армия", Theme.Label(28, Theme.Text, display: true));
        cy += 46;

        // Ready-made armies.
        var presets = Army.Presets;
        float presetWidth = inner / presets.Count;
        for (int i = 0; i < presets.Count; i++)
        {
            if (Theme.Button(new Rect(cx + i * presetWidth + 3, cy, presetWidth - 6, 40), presets[i].Name, Theme.PanelLight, 15))
            {
                SetEditArmy(presets[i].Army);
                SaveMenuSettings();
            }
        }
        cy += 54;

        // Two home ranks: rank 2 on top, rank 1 at the bottom, as White sees them.
        float boardLeft = cx + (inner - cell * 8) / 2;
        for (int rank = 1; rank >= 0; rank--)
        {
            for (int file = 0; file < 8; file++)
            {
                var square = new Square(file, rank);
                var rect = new Rect(boardLeft + file * cell, cy + (1 - rank) * cell, cell, cell);
                Theme.Fill(rect, (file + rank) % 2 == 0 ? DarkSquare : LightSquare);
                bool hover = rect.Contains(Event.current.mousePosition);
                if (hover)
                    Theme.Fill(rect, new Color(1, 1, 1, 0.25f));
                if (_editArmy.TryGetValue(square, out var type))
                {
                    var texture = PieceTexture(type, PieceColor.White);
                    if (texture != null)
                        GUI.DrawTexture(rect, texture);
                }
                if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
                    EditSquare(square);
            }
        }
        for (int file = 0; file < 8; file++)
            GUI.Label(new Rect(boardLeft + file * cell, cy + cell * 2, cell, 22), ((char)('a' + file)).ToString(),
                Theme.Label(13, Theme.Muted, TextAnchor.MiddleCenter));
        cy += cell * 2 + 30;

        // Palette: pick a piece, then click squares. The last button erases.
        float toolsLeft = cx + (inner - (tool + 6) * 7) / 2;
        for (int i = 0; i <= ArmyTools.Length; i++)
        {
            PieceType? type = i < ArmyTools.Length ? ArmyTools[i] : (PieceType?)null;
            var rect = new Rect(toolsLeft + i * (tool + 6), cy, tool, tool + 10);
            bool selected = _armyTool == type;
            bool hover = rect.Contains(Event.current.mousePosition);
            Theme.Fill(rect, selected ? Theme.WithAlpha(Theme.Gold, 0.3f) : hover ? Theme.PanelLight : Theme.WithAlpha(Theme.PanelLight, 0.5f), 12);
            if (selected)
                Theme.Outline(rect, Theme.Gold, 3, 12);
            if (type.HasValue)
            {
                var texture = PieceTexture(type.Value, PieceColor.White);
                if (texture != null)
                    GUI.DrawTexture(new Rect(rect.x + 6, rect.y + 2, tool - 12, tool - 12), texture);
                string price = type.Value == PieceType.King ? "король" : PieceValues.Of(type.Value).ToString();
                GUI.Label(new Rect(rect.x, rect.yMax - 24, rect.width, 22), price, Theme.Label(14, Theme.Gold, TextAnchor.MiddleCenter, bold: true));
            }
            else
            {
                GUI.Label(new Rect(rect.x, rect.y, rect.width, rect.height), "Убрать", Theme.Label(14, Theme.Text, TextAnchor.MiddleCenter, bold: true));
            }
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                _armyTool = type;
                Sfx.Play(Sfx.Click);
            }
        }
        cy += tool + 22;

        // Budget and checks.
        var army = PlayerArmy();
        int cost = army.Cost;
        GUI.Label(new Rect(cx, cy, inner / 2, 24), $"Очки: {cost} из {Army.Budget}", Theme.Label(18, cost > Army.Budget ? Theme.Red : Theme.Text, bold: true));
        GUI.Label(new Rect(cx + inner / 2, cy, inner / 2, 24), $"Фигур: {army.Pieces.Count} из {Army.MaxPieces}",
            Theme.Label(16, Theme.Muted, TextAnchor.MiddleRight));
        Theme.Bar(new Rect(cx, cy + 28, inner, 14), (float)cost / Army.Budget, cost > Army.Budget ? Theme.Red : Theme.Gold);
        var errors = army.Validate();
        string verdict = errors.Count == 0 ? "Армия готова к бою!" : ArmyErrorText(errors[0]);
        GUI.Label(new Rect(cx, cy + 46, inner, 22), verdict, Theme.Label(15, errors.Count == 0 ? Theme.Green : Theme.Red, bold: true));
        cy += 76;

        // Bot settings for a game against the bot.
        if (wide)
        {
            _menuColor = Theme.Pills(new Rect(cx, cy, inner / 2 - 6, 40), _menuColor, ColorNames, Theme.Orange, 15);
            _level = (BotLevel)Theme.Pills(new Rect(cx + inner / 2 + 6, cy, inner / 2 - 6, 40), (int)_level, LevelNames, Theme.Orange, 15);
            cy += 56;
        }
        else
        {
            _menuColor = Theme.Pills(new Rect(cx, cy, inner, 40), _menuColor, ColorNames, Theme.Orange, 15);
            _level = (BotLevel)Theme.Pills(new Rect(cx, cy + 50, inner, 40), (int)_level, LevelNames, Theme.Orange, 15);
            cy += 104;
        }

        if (Theme.Button(new Rect(cx, cy, inner / 2 - 6, 56), "Против бота", Theme.Green, 20, errors.Count == 0))
        {
            SaveMenuSettings();
            StartBotGame(false, army);
        }
        if (Theme.Button(new Rect(cx + inner / 2 + 6, cy, inner / 2 - 6, 56), "Онлайн", Theme.Teal, 20, errors.Count == 0))
        {
            SaveMenuSettings();
            _onlineMode = 1;
            ShowPage(Page.Online);
        }
    }

    private void EditSquare(Square square)
    {
        if (_armyTool == null)
        {
            _editArmy.Remove(square);
        }
        else if (_editArmy.TryGetValue(square, out var current) && current == _armyTool.Value && current != PieceType.King)
        {
            // Clicking a piece with the same tool takes it away again.
            _editArmy.Remove(square);
        }
        else
        {
            var type = _armyTool.Value;
            if (type == PieceType.Pawn && square.Rank == 0)
            {
                ShowToast("Пешки ставятся только во второй ряд.");
                return;
            }
            if (type == PieceType.King)
            {
                // There is one king: placing it moves it.
                var kings = new List<Square>();
                foreach (var pair in _editArmy)
                    if (pair.Value == PieceType.King)
                        kings.Add(pair.Key);
                foreach (var king in kings)
                    _editArmy.Remove(king);
            }
            _editArmy[square] = type;
        }
        Sfx.Play(Sfx.Move);
        SaveMenuSettings();
    }
}
