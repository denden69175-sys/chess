#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chess.Core;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Our two party modes, both drawn on an IMGUI board: "Союзники" (2 vs 2 on one board, each player
/// commands one flank of the team's army) and "Тройной трон" (three armies on a 12×12 board, a captured
/// king hands its army to the captor).
/// </summary>
public partial class ChessBoardView
{
    private static readonly Color TeamColor = new Color32(84, 112, 236, 255);
    private static readonly Color ThreeColor = new Color32(222, 86, 62, 255);
    private static readonly Color QueenFlank = new Color32(255, 204, 77, 255);
    private static readonly Color KingFlank = new Color32(32, 196, 196, 255);
    private static readonly Color RedArmy = new Color32(236, 92, 80, 255);
    private static readonly Color[] ThreeColors = { new Color32(240, 238, 250, 255), new Color32(236, 92, 80, 255), new Color32(70, 64, 96, 255) };

    private static readonly string[] TeamSeatNames = { "Белые · ферзь", "Чёрные · ферзь", "Белые · король", "Чёрные · король" };
    private static readonly string[] TeamSeatPills = { "Белые 1", "Чёрные 1", "Белые 2", "Чёрные 2" };
    private static readonly string[] PartyOpponents = { "С ботами", "Все люди" };
    private static readonly string[] ThreeNames = { "Белые", "Красные", "Чёрные" };
    private static readonly string[] ThreeSeatPills = { "Белые (низ)", "Красные (слева)", "Чёрные (справа)" };
    private static readonly string[] ThreeGenitive = { "белых", "красных", "чёрных" };

    private const string TeamRules =
        "<b>Команды</b>\n" +
        "•  Белые против чёрных, в каждой команде по два игрока. Ходят по кругу: Белые 1, Чёрные 1, Белые 2, Чёрные 2.\n" +
        "•  Игрок 1 командует ферзевым флангом: фигуры, которые стоят на линиях a–d, вместе с ферзём (золотая метка).\n" +
        "•  Игрок 2 командует королевским флангом: линии e–h вместе с королём (бирюзовая метка). Рокировка тоже его.\n" +
        "•  Ходить можно только своими фигурами. Пешка, которая дошла до конца, остаётся у того, кто её довёл.\n\n" +
        "<b>Выручка</b>\n" +
        "•  Если король твоей команды под шахом, в свой ход можно сходить любой фигурой команды, даже фигурой напарника.\n" +
        "•  Если своими фигурами ходить некуда, тоже можно взять любую фигуру команды.\n\n" +
        "<b>Конец партии</b>\n" +
        "•  Мат, пат и ничьи — как в обычных шахматах, но для всей команды сразу. Мат ставит любой игрок команды.\n" +
        "•  Совет: договаривайтесь с напарником. Ферзевый фланг атакует, королевский бережёт короля.";

    private const string ThreeRules =
        "<b>Поле и армии</b>\n" +
        "•  Доска 12×12. Белые стоят внизу, Красные слева, Чёрные справа, верхний край пустой.\n" +
        "•  Ходят по кругу: Белые, Красные, Чёрные. Фигуры ходят как обычно. Пешки идут к противоположному краю (белые вверх, красные вправо, чёрные влево), бьют наискосок вперёд и в конце становятся ферзём. Рокировки и взятия на проходе нет.\n\n" +
        "<b>Без шахов</b>\n" +
        "•  Шаха и мата нет: короля можно просто съесть. Поэтому следи за своим королём сам — игра подсветит его красным, если на него напали.\n\n" +
        "<b>Вербовка</b>\n" +
        "•  Кто съел короля, тот забирает всю армию этого игрока: его фигуры перекрашиваются и дальше ходят за победителя. Пешки при этом идут в свою прежнюю сторону.\n" +
        "•  Выбывший игрок больше не ходит.\n\n" +
        "<b>Конец партии</b>\n" +
        "•  Побеждает последний оставшийся король.\n" +
        "•  Если ходить нечем, ход пропускается. Если 150 ходов подряд никто ничего не съел и не двинул пешку — ничья.";

    // Shared state of both modes.
    private bool _partyRules;
    private Vector2 _partyRulesScroll;
    private int _partyOpponent;
    private bool _partyEndShown;
    private bool _partyEndHidden;
    private float _partyEndAt;
    private float _partyBotReadyAt;

    // 2 vs 2.
    private TeamGame _team = new TeamGame();
    private int _teamSeat;
    private Square? _teamSelected;
    private readonly List<Move> _teamTargets = new List<Move>();
    private Task<Move?>? _teamBotTask;

    // Three players.
    private ThreeGame _three = new ThreeGame();
    private int _threeSeat;
    private int? _threeSelected;
    private readonly List<TriMove> _threeTargets = new List<TriMove>();
    private Task<TriMove?>? _threeBotTask;
    private string _threeNews = "";
    private float _threeNewsAt = -100;

    private bool TeamHuman(int player) => _partyOpponent == 1 || player == _teamSeat;
    private bool ThreeHuman(int player) => _partyOpponent == 1 || player == _threeSeat;

    private void CancelPartyBots()
    {
        _teamBotTask = null;
        _threeBotTask = null;
    }

    // ---------------------------------------------------------------- setup screens

    private void DrawPartySetup(float width, float height, bool three)
    {
        Theme.InputBlocked = _partyRules;
        BackButton();
        var accent = three ? ThreeColor : TeamColor;
        float cardWidth = Mathf.Min(600, width - 32);
        float x = (width - cardWidth) / 2;
        float cardHeight = 120 + 84 + 84 + 190 + 82;
        float y = Mathf.Max(70, (height - cardHeight) / 2);
        Theme.PanelBox(new Rect(x, y, cardWidth, cardHeight), 24);

        float inner = cardWidth - 48;
        float cx = x + 24;
        float cy = y + 20;
        if (_pieceTextures.TryGetValue(three ? "bR" : "wR", out var icon))
            GUI.DrawTexture(new Rect(cx, cy - 4, 56, 56), icon);
        GUI.Label(new Rect(cx + 66, cy, inner - 66, 48), three ? "Тройной трон" : "Союзники: 2 на 2", Theme.Label(28, Theme.Text, display: true));
        cy += 66;

        GUI.Label(new Rect(cx, cy, inner, 24), "Кто играет", Theme.Label(16, Theme.Muted, bold: true));
        _partyOpponent = Theme.Pills(new Rect(cx, cy + 26, inner, 44), _partyOpponent, PartyOpponents, accent);
        cy += 84;
        bool bots = _partyOpponent == 0;
        GUI.Label(new Rect(cx, cy, inner, 24), bots ? "Ваше место" : "Все ходят по очереди на этом устройстве", Theme.Label(16, Theme.Muted, bold: true));
        if (bots)
        {
            if (three)
                _threeSeat = Theme.Pills(new Rect(cx, cy + 26, inner, 44), _threeSeat, ThreeSeatPills, accent, 15);
            else
                _teamSeat = Theme.Pills(new Rect(cx, cy + 26, inner, 44), _teamSeat, TeamSeatPills, accent, 15);
        }
        cy += 84;

        var rules = new Rect(cx, cy, inner, 176);
        Theme.Fill(rules, Theme.WithAlpha(accent, 0.16f), 14);
        Theme.Outline(rules, Theme.WithAlpha(accent, 0.6f), 2, 14);
        string text = three
            ? "•  Три армии на доске 12×12, ходят по кругу.\n•  Шахов нет: короля можно съесть.\n•  Кто съел короля, забирает всю его армию.\n•  Побеждает последний король."
            : "•  Две команды по два игрока, ходят по кругу.\n•  Игрок 1 ведёт ферзевый фланг (a–d), игрок 2 — королевский (e–h) с королём.\n•  Ходи только своими фигурами, но при шахе можно спасать короля любой фигурой команды.";
        GUI.Label(new Rect(rules.x + 14, rules.y + 8, rules.width - 28, rules.height - 16), text,
            Theme.Label(16, Theme.Text, TextAnchor.MiddleLeft, wrap: true));
        cy += 190;

        if (Theme.Button(new Rect(cx, cy, inner / 3 - 6, 62), "Правила", accent, 20))
            _partyRules = true;
        if (Theme.Button(new Rect(cx + inner / 3 + 6, cy, inner * 2 / 3 - 6, 62), "ИГРАТЬ", Theme.Green, 26))
        {
            SaveMenuSettings();
            if (three)
                StartThree();
            else
                StartTeam();
        }

        Theme.InputBlocked = false;
        if (_partyRules)
            DrawPartyRules(width, height, three);
    }

    private void DrawPartyRules(float width, float height, bool three)
    {
        Dim(width, height, 0.7f);
        var accent = three ? ThreeColor : TeamColor;
        float cardWidth = Mathf.Min(680, width - 24);
        float cardHeight = Mathf.Min(620, height - 40);
        var card = new Rect((width - cardWidth) / 2, (height - cardHeight) / 2, cardWidth, cardHeight);
        Theme.Card(card, Theme.Darken(accent, 0.7f), 24);
        GUI.Label(new Rect(card.x, card.y + 14, card.width, 44), three ? "Правила «Тройного трона»" : "Правила «Союзников»",
            Theme.Label(26, Theme.Gold, TextAnchor.MiddleCenter, display: true));

        var style = Theme.Label(16, Theme.Text, TextAnchor.UpperLeft, wrap: true);
        style.richText = true;
        string text = three ? ThreeRules : TeamRules;
        var view = new Rect(card.x + 20, card.y + 66, card.width - 40, card.height - 66 - 84);
        float textHeight = style.CalcHeight(new GUIContent(text), view.width - 24);
        _partyRulesScroll = GUI.BeginScrollView(view, _partyRulesScroll, new Rect(0, 0, view.width - 24, textHeight));
        GUI.Label(new Rect(0, 0, view.width - 24, textHeight), text, style);
        GUI.EndScrollView();

        if (Theme.Button(new Rect(card.x + (card.width - 220) / 2, card.yMax - 72, 220, 52), "Понятно", Theme.Green, 20))
            _partyRules = false;
    }

    // ---------------------------------------------------------------- shared game frame

    /// <summary>Top bar and status line; returns false when the player left the game.</summary>
    private bool DrawPartyTop(float width, string title, Color accent, string status, Color statusOutline, System.Action restart)
    {
        if (Theme.Button(new Rect(12, 12, 118, 44), "‹ Меню", Theme.PanelLight, 18))
        {
            Theme.InputBlocked = false;
            ShowPage(Page.Home);
            return false;
        }
        float right = width - 12;
        if (Theme.Button(new Rect(right - 140, 12, 140, 44), "Заново", Theme.Blue, 18))
            restart();
        if (Theme.Button(new Rect(right - 292, 12, 140, 44), "Правила", accent, 18))
            _partyRules = true;
        if (width >= 760)
            GUI.Label(new Rect(140, 12, width - 444, 44), title, Theme.Label(24, accent, TextAnchor.MiddleCenter, display: true));

        float statusWidth = Mathf.Min(620, width - 24);
        var rect = new Rect((width - statusWidth) / 2, 64, statusWidth, 40);
        Theme.Fill(rect, Theme.Panel, 20);
        Theme.Outline(rect, statusOutline, 2, 20);
        GUI.Label(rect, status, Theme.Label(statusWidth < 500 ? 15 : 17, Theme.Text, TextAnchor.MiddleCenter, bold: true));
        return true;
    }

    private void DrawPartyResult(float width, float height, string title, string reason, Color accent, System.Action again)
    {
        Dim(width, height, 0.55f);
        float appear = Mathf.Clamp01((Time.time - _partyEndAt) / 0.35f);
        float cardWidth = Mathf.Min(480, width - 24);
        var card = new Rect((width - cardWidth) / 2, (height - 300) / 2 - (1 - appear) * 40, cardWidth, 300);
        GUI.color = new Color(1, 1, 1, appear);
        Theme.Card(card, Theme.Darken(accent, 0.6f), 26);
        Theme.ShadowText(new Rect(card.x, card.y + 30, card.width, 60), title,
            Theme.Label(title.Length > 14 ? 28 : 40, accent, TextAnchor.MiddleCenter, display: true), 3);
        GUI.Label(new Rect(card.x + 20, card.y + 96, card.width - 40, 40), reason, Theme.Label(18, Theme.Text, TextAnchor.MiddleCenter, wrap: true));
        float buttonWidth = (cardWidth - 60) / 2;
        if (Theme.Button(new Rect(card.x + 20, card.y + 150, buttonWidth, 54), "Ещё раз", Theme.Green, 19))
            again();
        if (Theme.Button(new Rect(card.x + 40 + buttonWidth, card.y + 150, buttonWidth, 54), "В меню", Theme.Blue, 19))
            ShowPage(Page.Home);
        if (GUI.Button(new Rect(card.x, card.yMax - 70, card.width, 40), "Посмотреть доску", Theme.Label(16, Theme.Muted, TextAnchor.MiddleCenter, bold: true)))
        {
            Theme.ClickTaken();
            _partyEndHidden = true;
        }
        GUI.color = Color.white;
    }

    private void UpdatePartyEnd(bool over, bool? humanWon)
    {
        if (over && !_partyEndShown && Time.time >= _partyEndAt)
        {
            _partyEndShown = true;
            Sfx.Play(humanWon == false ? Sfx.Lose : Sfx.Win);
        }
    }

    /// <summary>The board rectangle for a square board of <paramref name="cells"/> cells, with room for a side panel when wide.</summary>
    private static Rect PartyBoardRect(float width, float height, int cells, out Rect panel)
    {
        float top = 116, bottom = 16;
        float available = height - top - bottom;
        bool wide = width - available >= 300;
        float size = wide ? available : Mathf.Min(available, width - 24);
        size = Mathf.Floor(size / cells) * cells;
        float x = wide ? Mathf.Max(12, (width - size - 290) / 2) : (width - size) / 2;
        var board = new Rect(x, top, size, size);
        panel = wide ? new Rect(board.xMax + 20, top, Mathf.Min(270, width - board.xMax - 32), size) : Rect.zero;
        return board;
    }

    private bool PartyClick(Rect board, out Vector2 point)
    {
        var e = Event.current;
        point = e.mousePosition;
        if (e.type != EventType.MouseDown || e.button != 0 || !board.Contains(point) || Theme.InputBlocked)
            return false;
        if (Time.unscaledTime - Theme.LastClickTime < 0.25f)
            return false;
        e.Use();
        return true;
    }

    private void DrawDot(Rect cell, bool capture, Color color)
    {
        if (capture)
        {
            Theme.Outline(cell, color, Mathf.Max(2, cell.width * 0.1f), cell.width / 2);
        }
        else
        {
            float dot = cell.width * 0.32f;
            Theme.Fill(new Rect(cell.center.x - dot / 2, cell.center.y - dot / 2, dot, dot), color, dot / 2);
        }
    }

    // ---------------------------------------------------------------- 2 vs 2

    private void StartTeam()
    {
        CancelPartyBots();
        _team = new TeamGame();
        _teamSelected = null;
        _teamTargets.Clear();
        _partyEndShown = false;
        _partyEndHidden = false;
        _partyRules = false;
        ShowPage(Page.Team);
        Sfx.Play(Sfx.Start);
        MaybeStartTeamBot();
    }

    private void MaybeStartTeamBot()
    {
        if (_team.IsOver || TeamHuman(_team.Player) || _teamBotTask != null)
            return;
        var position = _team.Current;
        var allowed = _team.LegalMoves();
        _teamBotTask = Task.Run(() => new Bot(BotLevel.Medium).ChooseMoveWithScore(position, allowed).Move);
        _partyBotReadyAt = Time.time + Random.Range(0.6f, 1.3f);
    }

    private void UpdateTeam()
    {
        if (_teamBotTask != null && _teamBotTask.IsCompleted && Time.time >= _partyBotReadyAt)
        {
            var task = _teamBotTask;
            _teamBotTask = null;
            if (task.Status == TaskStatus.RanToCompletion && task.Result.HasValue && _team.IsLegal(task.Result.Value))
                PlayTeamMove(task.Result.Value);
            else if (task.IsFaulted)
                Debug.LogException(task.Exception);
        }
        UpdatePartyEnd(_team.IsOver, TeamHumanWon);
    }

    private bool? TeamHumanWon =>
        _partyOpponent == 1 || _team.Winner == null ? (bool?)null : _team.Winner == TeamGame.ColorOf(_teamSeat);

    private void PlayTeamMove(Move move)
    {
        bool capture = _team.Current[move.To].HasValue || move.Kind == MoveKind.EnPassant;
        _team.MakeMove(move);
        _teamSelected = null;
        _teamTargets.Clear();
        Sfx.Play(_team.Current.IsInCheck(_team.Current.SideToMove) ? Sfx.Check : capture ? Sfx.Capture : Sfx.Move);
        if (_team.IsOver)
            _partyEndAt = Time.time + 0.9f;
        else
            MaybeStartTeamBot();
    }

    private string TeamPlayerName(int player)
    {
        string name = TeamSeatNames[player];
        if (_partyOpponent == 1)
            return name;
        if (player == _teamSeat)
            return name + " (вы)";
        return name + (TeamGame.ColorOf(player) == TeamGame.ColorOf(_teamSeat) ? " (напарник-бот)" : " (бот)");
    }

    private string TeamStatus()
    {
        if (_team.IsOver)
            return TeamResultReason();
        int player = _team.Player;
        string who = TeamHuman(player) && _partyOpponent == 0 ? "Ваш ход" : "Ходит: " + TeamPlayerName(player);
        if (_teamBotTask != null)
            who += "...";
        if (_team.Current.IsInCheck(_team.Current.SideToMove))
            return "Шах! " + who + " — выручка: можно любой фигурой команды";
        return who;
    }

    private string TeamResultReason() => _team.Status switch
    {
        GameStatus.Checkmate => $"Мат. Победила команда {(_team.Winner == PieceColor.White ? "белых" : "чёрных")}.",
        GameStatus.Stalemate => "Пат. Ничья.",
        GameStatus.FiftyMoveRule => "Правило 50 ходов. Ничья.",
        GameStatus.ThreefoldRepetition => "Позиция повторилась три раза. Ничья.",
        GameStatus.InsufficientMaterial => "Никто не может поставить мат. Ничья.",
        _ => "",
    };

    private void DrawTeam(float width, float height)
    {
        Theme.Gradient(new Rect(0, 0, width, height), Theme.BackgroundTop, Theme.BackgroundBottom);
        bool window = (_partyEndShown && !_partyEndHidden) || _partyRules;
        Theme.InputBlocked = window;

        bool check = !_team.IsOver && _team.Current.IsInCheck(_team.Current.SideToMove);
        var outline = check ? Theme.Red : _team.IsOver ? Theme.Gold : TeamGame.IsKingSide(_team.Player) ? KingFlank : QueenFlank;
        if (!DrawPartyTop(width, "Союзники: 2 на 2", TeamColor, TeamStatus(), outline, StartTeam))
            return;

        var board = PartyBoardRect(width, height, 8, out var panel);
        bool flip = _partyOpponent == 0 && TeamGame.ColorOf(_teamSeat) == PieceColor.Black;
        float cell = board.width / 8;
        Rect CellRect(Square s) => new Rect(board.x + (flip ? 7 - s.File : s.File) * cell, board.y + (flip ? s.Rank : 7 - s.Rank) * cell, cell, cell);

        // Input.
        bool mayMove = !_team.IsOver && TeamHuman(_team.Player) && _teamBotTask == null;
        if (PartyClick(board, out var point))
        {
            int column = Mathf.Clamp((int)((point.x - board.x) / cell), 0, 7), row = Mathf.Clamp((int)((point.y - board.y) / cell), 0, 7);
            var square = new Square(flip ? 7 - column : column, flip ? row : 7 - row);
            var target = _teamTargets.Where(m => m.To == square).OrderBy(m => m.Promotion == PieceType.Queen ? 0 : 1).FirstOrDefault();
            if (_teamSelected.HasValue && _teamTargets.Any(m => m.To == square))
            {
                PlayTeamMove(target);
            }
            else if (mayMove && _team.LegalMoves().Any(m => m.From == square) && _teamSelected != square)
            {
                _teamSelected = square;
                _teamTargets.Clear();
                _teamTargets.AddRange(_team.LegalMoves().Where(m => m.From == square));
                Sfx.Play(Sfx.Click);
            }
            else
            {
                _teamSelected = null;
                _teamTargets.Clear();
            }
        }

        // Board.
        Theme.Fill(new Rect(board.x - 8, board.y - 8, board.width + 16, board.height + 16), Theme.Panel, 12);
        GUI.DrawTexture(board, Checker());
        if (_team.Game.Moves.Count > 0)
        {
            var last = _team.Game.Moves[_team.Game.Moves.Count - 1];
            Theme.Fill(CellRect(last.From), LastMoveTint);
            Theme.Fill(CellRect(last.To), LastMoveTint);
        }
        if (check)
        {
            var king = _team.Current.FindKing(_team.Current.SideToMove);
            if (king.HasValue)
                Theme.Fill(CellRect(king.Value), CheckTint);
        }
        if (_teamSelected.HasValue)
            Theme.Fill(CellRect(_teamSelected.Value), SelectedTint);

        var movable = mayMove ? new HashSet<Square>(_team.LegalMoves().Select(m => m.From)) : new HashSet<Square>();
        var mover = _team.Current.SideToMove;
        for (int i = 0; i < 64; i++)
        {
            var piece = _team.Current[i];
            if (!piece.HasValue || !_pieceTextures.TryGetValue(PieceKey(piece.Value), out var texture))
                continue;
            var square = new Square(i);
            var rect = CellRect(square);
            // Team pieces the player to move may not touch are shown faded.
            bool faded = mayMove && piece.Value.Color == mover && !movable.Contains(square) && _team.OwnerAt(square) != _team.Player;
            GUI.color = new Color(1, 1, 1, faded ? 0.45f : 1);
            GUI.DrawTexture(rect, texture);
            GUI.color = Color.white;
            var owner = _team.OwnerAt(square);
            if (owner.HasValue)
            {
                float mark = Mathf.Max(7, cell * 0.17f);
                var dot = new Rect(rect.xMax - mark - 3, rect.yMax - mark - 3, mark, mark);
                Theme.Fill(dot, new Color(0, 0, 0, 0.5f), mark / 2);
                Theme.Fill(new Rect(dot.x + 1.5f, dot.y + 1.5f, mark - 3, mark - 3), TeamGame.IsKingSide(owner.Value) ? KingFlank : QueenFlank, mark / 2);
            }
        }
        foreach (var move in _teamTargets)
            DrawDot(CellRect(move.To), _team.Current[move.To].HasValue, TargetDot);

        // Players.
        if (panel.width > 0)
        {
            Theme.PanelBox(panel, 18);
            GUI.Label(new Rect(panel.x + 16, panel.y + 12, panel.width - 32, 28), "Игроки", Theme.Label(20, Theme.Text, bold: true));
            for (int p = 0; p < TeamGame.Players; p++)
            {
                var row = new Rect(panel.x + 12, panel.y + 50 + p * 74, panel.width - 24, 66);
                bool turn = !_team.IsOver && _team.Player == p;
                Theme.Fill(row, turn ? Theme.WithAlpha(Theme.Gold, 0.22f) : Theme.WithAlpha(Theme.PanelLight, 0.6f), 12);
                if (turn)
                    Theme.Outline(row, Theme.Gold, 2, 12);
                var chip = new Rect(row.x + 10, row.y + 9, 24, 24);
                Theme.Fill(chip, TeamGame.ColorOf(p) == PieceColor.White ? WhiteFrame : BlackFrame, 12);
                Theme.Outline(chip, TeamGame.IsKingSide(p) ? KingFlank : QueenFlank, 3, 12);
                GUI.Label(new Rect(row.x + 42, row.y + 2, row.width - 46, 38), TeamPlayerName(p), Theme.Label(15, Theme.Text, wrap: true));
                DrawTaken(new Rect(row.x + 10, row.y + 40, row.width - 20, 22), TakenIcons(_team.CapturedBy(p)));
            }
            float ly = panel.y + 50 + 4 * 74 + 6;
            GUI.Label(new Rect(panel.x + 16, ly, panel.width - 32, 80),
                "Золотая метка — ферзевый фланг (игрок 1), бирюзовая — королевский (игрок 2).", Theme.Label(14, Theme.Muted, TextAnchor.UpperLeft, wrap: true));
        }

        Theme.InputBlocked = false;
        if (_partyRules)
            DrawPartyRules(width, height, false);
        else if (_partyEndShown && !_partyEndHidden)
        {
            var won = TeamHumanWon;
            string title = _team.Winner == null ? "НИЧЬЯ" : _partyOpponent == 1
                ? (_team.Winner == PieceColor.White ? "ПОБЕДИЛИ БЕЛЫЕ" : "ПОБЕДИЛИ ЧЁРНЫЕ")
                : won == true ? "ПОБЕДА!" : "ПОРАЖЕНИЕ";
            DrawPartyResult(width, height, title, TeamResultReason(), won == false ? Theme.Red : Theme.Gold, StartTeam);
        }
    }

    // ---------------------------------------------------------------- three players

    private void StartThree()
    {
        CancelPartyBots();
        _three = new ThreeGame();
        _threeSelected = null;
        _threeTargets.Clear();
        _threeNews = "";
        _partyEndShown = false;
        _partyEndHidden = false;
        _partyRules = false;
        ShowPage(Page.Three);
        Sfx.Play(Sfx.Start);
        MaybeStartThreeBot();
    }

    private void MaybeStartThreeBot()
    {
        if (_three.IsOver || ThreeHuman(_three.Player) || _threeBotTask != null)
            return;
        var board = _three.BoardCopy();
        int player = _three.Player;
        _threeBotTask = Task.Run(() => new ThreeBot().Choose(board, player));
        _partyBotReadyAt = Time.time + Random.Range(0.5f, 1.0f);
    }

    private void UpdateThree()
    {
        if (_threeBotTask != null && _threeBotTask.IsCompleted && Time.time >= _partyBotReadyAt)
        {
            var task = _threeBotTask;
            _threeBotTask = null;
            if (task.Status == TaskStatus.RanToCompletion && task.Result.HasValue && _three.IsLegal(task.Result.Value))
                PlayThreeMove(task.Result.Value);
            else if (task.IsFaulted)
                Debug.LogException(task.Exception);
        }
        bool? won = _partyOpponent == 1 || !_three.Winner.HasValue ? (bool?)null : _three.Winner == _threeSeat;
        if (_partyOpponent == 0 && !_three.IsAlive(_threeSeat) && !_three.IsOver)
            won = false;
        UpdatePartyEnd(_three.IsOver || (_partyOpponent == 0 && !_three.IsAlive(_threeSeat)), won);
    }

    private void PlayThreeMove(TriMove move)
    {
        bool capture = _three[move.To].HasValue;
        int player = _three.Player;
        _three.MakeMove(move);
        _threeSelected = null;
        _threeTargets.Clear();
        if (_three.LastEliminated.HasValue)
        {
            int loser = _three.LastEliminated.Value;
            _threeNews = $"{ThreeNames[player]} съели короля {ThreeGenitive[loser]} и забрали их армию!";
            _threeNewsAt = Time.time;
            Sfx.Play(Sfx.Jump);
        }
        else
        {
            Sfx.Play(capture ? Sfx.Capture : Sfx.Move);
        }
        bool humanOut = _partyOpponent == 0 && !_three.IsAlive(_threeSeat);
        if (_three.IsOver || humanOut)
            _partyEndAt = Time.time + 0.9f;
        if (!_three.IsOver)
            MaybeStartThreeBot();
    }

    private string ThreePlayerName(int player)
    {
        string name = ThreeNames[player];
        if (_partyOpponent == 1)
            return name;
        return name + (player == _threeSeat ? " (вы)" : " (бот)");
    }

    private string ThreeStatus()
    {
        if (_three.IsOver)
            return _three.Winner.HasValue ? $"Победили {ThreeNames[_three.Winner.Value]}!" : "Ничья: 150 ходов без взятий.";
        if (Time.time - _threeNewsAt < 4)
            return _threeNews;
        int player = _three.Player;
        var king = _three.KingSquare(player);
        bool danger = king.HasValue && _three.IsAttacked(king.Value, player);
        string who = _partyOpponent == 0 && player == _threeSeat ? "Ваш ход" : "Ходят " + ThreePlayerName(player) + (_threeBotTask != null ? "..." : "");
        return danger ? who + " — король под ударом!" : who;
    }

    private void DrawThree(float width, float height)
    {
        Theme.Gradient(new Rect(0, 0, width, height), Theme.BackgroundTop, Theme.BackgroundBottom);
        bool window = (_partyEndShown && !_partyEndHidden) || _partyRules;
        Theme.InputBlocked = window;

        var outline = _three.IsOver ? Theme.Gold : _three.Player == 2 ? Theme.Muted : ThreeColors[_three.Player];
        if (!DrawPartyTop(width, "Тройной трон", ThreeColor, ThreeStatus(), outline, StartThree))
            return;

        int n = ThreeGame.Size;
        var board = PartyBoardRect(width, height, n, out var panel);
        float cell = board.width / n;
        Rect CellRect(int s) => new Rect(board.x + ThreeGame.FileOf(s) * cell, board.y + (n - 1 - ThreeGame.RankOf(s)) * cell, cell, cell);

        bool mayMove = !_three.IsOver && ThreeHuman(_three.Player) && _threeBotTask == null
            && (_partyOpponent == 1 || _three.IsAlive(_threeSeat));
        if (PartyClick(board, out var point))
        {
            int file = Mathf.Clamp((int)((point.x - board.x) / cell), 0, n - 1);
            int rank = n - 1 - Mathf.Clamp((int)((point.y - board.y) / cell), 0, n - 1);
            int square = ThreeGame.Index(file, rank);
            if (_threeSelected.HasValue && _threeTargets.Any(m => m.To == square))
            {
                PlayThreeMove(_threeTargets.First(m => m.To == square));
            }
            else if (mayMove && _three[square].HasValue && _three[square]!.Value.Owner == _three.Player && _threeSelected != square)
            {
                _threeSelected = square;
                _threeTargets.Clear();
                _threeTargets.AddRange(_three.MovesFrom(square));
                Sfx.Play(Sfx.Click);
            }
            else
            {
                _threeSelected = null;
                _threeTargets.Clear();
            }
        }

        Theme.Fill(new Rect(board.x - 8, board.y - 8, board.width + 16, board.height + 16), Theme.Panel, 12);
        for (int s = 0; s < ThreeGame.Squares; s++)
            Theme.Fill(CellRect(s), (ThreeGame.FileOf(s) + ThreeGame.RankOf(s)) % 2 == 0 ? DarkSquare : LightSquare);
        if (_three.LastMove.HasValue)
        {
            Theme.Fill(CellRect(_three.LastMove.Value.From), LastMoveTint);
            Theme.Fill(CellRect(_three.LastMove.Value.To), LastMoveTint);
        }
        if (_threeSelected.HasValue)
            Theme.Fill(CellRect(_threeSelected.Value), SelectedTint);
        for (int p = 0; p < ThreeGame.PlayerCount; p++)
        {
            var king = _three.KingSquare(p);
            if (king.HasValue && _three.IsAlive(p) && _three.IsAttacked(king.Value, p))
                Theme.Fill(CellRect(king.Value), CheckTint);
        }

        for (int s = 0; s < ThreeGame.Squares; s++)
        {
            var piece = _three[s];
            if (!piece.HasValue)
                continue;
            int owner = piece.Value.Owner;
            var color = owner == 2 ? PieceColor.Black : PieceColor.White;
            var texture = PieceTexture(piece.Value.Type, color);
            if (texture == null)
                continue;
            GUI.color = owner == 1 ? RedArmy : Color.white;
            GUI.DrawTexture(CellRect(s), texture);
            GUI.color = Color.white;
            if (piece.Value.Army != owner)
            {
                // A recruited piece keeps a small badge of its old army.
                float mark = Mathf.Max(6, cell * 0.2f);
                var r = CellRect(s);
                var dot = new Rect(r.xMax - mark - 2, r.yMax - mark - 2, mark, mark);
                Theme.Fill(dot, new Color(0, 0, 0, 0.5f), mark / 2);
                Theme.Fill(new Rect(dot.x + 1.5f, dot.y + 1.5f, mark - 3, mark - 3), ThreeColors[piece.Value.Army], mark / 2);
            }
        }
        foreach (var move in _threeTargets)
            DrawDot(CellRect(move.To), _three[move.To].HasValue, TargetDot);

        if (panel.width > 0)
        {
            Theme.PanelBox(panel, 18);
            GUI.Label(new Rect(panel.x + 16, panel.y + 12, panel.width - 32, 28), "Игроки", Theme.Label(20, Theme.Text, bold: true));
            for (int p = 0; p < ThreeGame.PlayerCount; p++)
            {
                var row = new Rect(panel.x + 12, panel.y + 50 + p * 92, panel.width - 24, 84);
                bool alive = _three.IsAlive(p);
                bool turn = !_three.IsOver && _three.Player == p;
                Theme.Fill(row, turn ? Theme.WithAlpha(Theme.Gold, 0.22f) : Theme.WithAlpha(Theme.PanelLight, alive ? 0.6f : 0.25f), 12);
                if (turn)
                    Theme.Outline(row, Theme.Gold, 2, 12);
                var chip = new Rect(row.x + 10, row.y + 13, 24, 24);
                Theme.Fill(chip, ThreeColors[p], 12);
                Theme.Outline(chip, Theme.Muted, 1.5f, 12);
                int count = Enumerable.Range(0, ThreeGame.Squares).Count(i => _three[i].HasValue && _three[i]!.Value.Owner == p);
                string text = ThreePlayerName(p) + "\n" + (alive ? $"фигур: {count}" : "выбыли");
                GUI.Label(new Rect(row.x + 42, row.y + 2, row.width - 46, 46), text, Theme.Label(15, alive ? Theme.Text : Theme.Muted, wrap: true));
                var taken = _three.CapturedBy(p).Select(t => (PieceTexture(t.Type, t.Owner == 2 ? PieceColor.Black : PieceColor.White), t.Owner == 1 ? RedArmy : Color.white, t.Type)).ToList();
                DrawTaken(new Rect(row.x + 10, row.y + 54, row.width - 20, 24), taken);
            }
            GUI.Label(new Rect(panel.x + 16, panel.y + 50 + 3 * 92 + 6, panel.width - 32, 120),
                $"Шахов нет: короля можно съесть, и тогда его армия переходит к вам.\nХодов без взятий: {_three.QuietMoves} из {ThreeGame.QuietLimit}.",
                Theme.Label(14, Theme.Muted, TextAnchor.UpperLeft, wrap: true));
        }

        Theme.InputBlocked = false;
        if (_partyRules)
            DrawPartyRules(width, height, true);
        else if (_partyEndShown && !_partyEndHidden)
        {
            bool humanOut = _partyOpponent == 0 && !_three.IsAlive(_threeSeat);
            string title;
            string reason;
            Color accent = Theme.Gold;
            if (_three.Winner.HasValue)
            {
                bool mine = _partyOpponent == 0 && _three.Winner == _threeSeat;
                title = _partyOpponent == 1 ? $"ПОБЕДИЛИ {ThreeNames[_three.Winner.Value].ToUpper()}" : mine ? "ПОБЕДА!" : "ПОРАЖЕНИЕ";
                reason = "Остался последний король.";
                if (_partyOpponent == 0 && !mine)
                    accent = Theme.Red;
            }
            else if (humanOut && !_three.IsOver)
            {
                title = "ПОРАЖЕНИЕ";
                reason = "Вашего короля съели, армия ушла к сопернику.";
                accent = Theme.Red;
            }
            else
            {
                title = "НИЧЬЯ";
                reason = "150 ходов без взятий и ходов пешками.";
                accent = Theme.Blue;
            }
            DrawPartyResult(width, height, title, reason, accent, StartThree);
        }
    }
}
