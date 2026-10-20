#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Chess.Core;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// 5D chess: every board of every timeline on one big canvas that can be dragged and zoomed.
/// Columns are moments of the game (plies), rows are timelines. Pieces move on the boards with a gold
/// frame, and also back in time and across timelines.
/// </summary>
public partial class ChessBoardView
{
    private static readonly Color FiveDColor = new Color32(214, 72, 160, 255);
    private static readonly Color WhiteFrame = new Color32(236, 234, 246, 255);
    private static readonly Color BlackFrame = new Color32(34, 30, 52, 255);
    private static readonly Color TravelDot = new Color32(150, 88, 245, 200);
    private static readonly Color TurnTint = new Color32(110, 170, 255, 150);

    // Board size and spacing on the canvas at zoom 1.
    private const float MdBoard = 176;
    private const float MdColumn = 224;
    private const float MdRow = 240;

    private const float MdBotMoveDelay = 0.55f;

    private MultiverseGame _md = new MultiverseGame();
    private int _fiveOpponent;

    private Rect _mdCanvas;
    private Vector2 _mdPan;
    private float _mdZoom = 1;
    private Vector2? _mdFocus;
    private bool _mdNeedFit;
    private Vector2? _mdPress;
    private Vector2 _mdLastMouse;
    private bool _mdDragging;

    private (int Timeline, Square Square)? _mdSelected;
    private readonly List<Move5D> _mdTargets = new List<Move5D>();
    private readonly Dictionary<(int, int), List<(Square, Color)>> _mdMarks = new Dictionary<(int, int), List<(Square, Color)>>();
    private bool _mdCanSubmit;
    private bool _mdPresentMoved;
    private string _mdSent = "";

    private Task<List<Move5D>?>? _mdBotTask;
    private float _mdBotReadyAt;
    private readonly Queue<Move5D> _mdBotMoves = new Queue<Move5D>();
    private float _mdBotNextAt;
    private bool _mdBotSubmit;

    private Texture2D? _checker;
    private bool _mdRules;
    private Vector2 _mdRulesScroll;

    private const string FiveDRules =
        "<b>Поле</b>\n" +
        "•  Каждая доска — позиция в какой-то момент партии. Слева направо идёт время: «Ход 1 · белые», «Ход 1 · чёрные» и так дальше.\n" +
        "•  Каждый ряд досок — отдельная линия времени. Её номер слева: 0 — основная, +1, +2… — линии белых (ниже), −1, −2… — линии чёрных (выше).\n" +
        "•  Рамка показывает, чей ход на доске: светлая — белых, тёмная — чёрных. Золотая рамка — на этой доске можно ходить сейчас.\n" +
        "•  Поле двигается мышью (зажать и тянуть), масштаб — колёсиком или кнопками «−» и «+». «К настоящему» возвращает к текущим доскам.\n\n" +
        "<b>Как сходить в прошлое</b>\n" +
        "•  Нажмите на свою фигуру на доске с золотой рамкой. Зелёные точки — обычные ходы. Фиолетовые точки на других досках — ходы в прошлое и на другие линии. Нажмите на точку.\n" +
        "•  Прыжок во времени: любая фигура, кроме короля и пешки, может перенестись на свою же клетку на любую более раннюю доску, где ходила та же сторона, или на ту же доску другой линии, если клетка там свободна. Поэтому фигура, которая ещё не сходила, прыгнуть не может: в прошлом на её клетке стоит она сама. Сначала сходите ей, а на следующем ходу отправляйте назад.\n" +
        "•  Кроме прыжка, фигуры ходят «пятимерно»: время и линии — это ещё два направления, кроме вертикали и горизонтали. Шаг назад во времени — на одну доску своей стороны влево, шаг по линиям — на ряд вверх или вниз. Ладья идёт по одному направлению, слон — ровно по двум сразу, ферзь и король — по любым, конь — на 2 по одному направлению и на 1 по другому.\n" +
        "•  Если фигура попала на доску в прошлом, с этой доски начинается новая линия времени. На ней фигура из будущего стоит вместе со всеми фигурами того момента, а с исходной доски она пропадает.\n" +
        "•  Если у стороны линий больше, чем у соперника, больше чем на одну, лишние линии бледнеют: они неактивны и не двигают «настоящее».\n\n" +
        "<b>Как закончить ход</b>\n" +
        "•  Фиолетовая полоса — «настоящее»: самый ранний момент среди последних досок активных линий. Сходите на всех досках, которые стоят в настоящем, тогда полоса перейдёт к сопернику и можно жать «Завершить ход».\n" +
        "•  На других досках с золотой рамкой ходить можно, но не обязательно.\n" +
        "•  «Отменить ход» откатывает ходы этого хода по одному.\n" +
        "•  Завершить ход нельзя, если после него соперник сможет взять любого вашего короля на любой доске, в том числе ходом через время.\n\n" +
        "<b>Конец партии</b>\n" +
        "•  Мат — когда любой вариант хода оставляет короля под ударом. Пат — ходить некуда, но король не под ударом: ничья.\n" +
        "•  Упрощения нашей версии: пешки по времени не ходят, пешка всегда превращается в ферзя.";

    private bool FiveDBotBusy => _mdBotTask != null || _mdBotMoves.Count > 0 || _mdBotSubmit;

    private bool FiveDPlayerMayMove =>
        _page == Page.FiveD && !_md.IsOver && !_onlineEnded && !_waitingForServer && !FiveDBotBusy
        && (_opponent == Opponent.SameDevice || _md.Mover == _human);

    private bool FlipFiveD => _opponent != Opponent.SameDevice && _human == PieceColor.Black;

    // ---------------------------------------------------------------- game flow

    private void StartFiveD()
    {
        CancelFiveDBot();
        _md = new MultiverseGame();
        _mdSelected = null;
        _mdTargets.Clear();
        _mdSent = "";
        _endShown = false;
        _endHidden = false;
        _onlineEnded = false;
        _waitingForServer = false;
        _mdNeedFit = true;
        _mdFocus = null;
        ShowPage(Page.FiveD);
        RefreshFiveD();
        Sfx.Play(Sfx.Start);
        MaybeStartFiveDBot();
    }

    private void CancelFiveDBot()
    {
        // A running search finishes on its own; its answer is ignored.
        _mdBotTask = null;
        _mdBotMoves.Clear();
        _mdBotSubmit = false;
    }

    private void MaybeStartFiveDBot()
    {
        if (_opponent != Opponent.Bot || _md.IsOver || _md.Mover == _human || FiveDBotBusy)
            return;
        var copy = _md.Clone();
        _mdBotTask = Task.Run(() => new MultiverseBot().ChooseTurn(copy));
        _mdBotReadyAt = Time.time + Random.Range(0.6f, 1.3f);
    }

    private void UpdateFiveD()
    {
        if (_mdFocus.HasValue)
        {
            _mdPan = Vector2.Lerp(_mdPan, _mdFocus.Value, 1 - Mathf.Exp(-8 * Time.deltaTime));
            if ((_mdPan - _mdFocus.Value).sqrMagnitude < 0.25f)
                _mdFocus = null;
        }

        if (_mdBotTask != null && _mdBotTask.IsCompleted && Time.time >= _mdBotReadyAt)
        {
            var task = _mdBotTask;
            _mdBotTask = null;
            if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
            {
                foreach (var move in task.Result)
                    _mdBotMoves.Enqueue(move);
                _mdBotNextAt = Time.time;
            }
            else
            {
                if (task.IsFaulted)
                    Debug.LogException(task.Exception);
                // The bot found no way out: it gives up.
                _onlineEnded = true;
                _onlineWinner = _human;
                _onlineReason = "resign";
                _endAt = Time.time + 0.3f;
            }
        }

        if (_mdBotMoves.Count > 0 && Time.time >= _mdBotNextAt)
        {
            PlayFiveDMove(_mdBotMoves.Dequeue(), true);
            _mdBotNextAt = Time.time + MdBotMoveDelay;
            if (_mdBotMoves.Count == 0)
                _mdBotSubmit = true;
        }
        else if (_mdBotSubmit && Time.time >= _mdBotNextAt)
        {
            _mdBotSubmit = false;
            if (_md.CanSubmit)
            {
                _md.Submit();
                AfterFiveDTurn();
            }
        }

        if (GameFinished && !_endShown && Time.time >= _endAt)
        {
            _endShown = true;
            Sfx.Play(PlayerWon == false ? Sfx.Lose : Sfx.Win);
        }
    }

    private void PlayFiveDMove(Move5D move, bool focus)
    {
        bool capture = _md.Timeline(move.ToTimeline)[move.ToPly][move.To].HasValue;
        int count = _md.Timelines.Count();
        _md.MakeMove(move);
        _mdSelected = null;
        _mdTargets.Clear();
        RefreshFiveD();
        Sfx.Play(capture ? Sfx.Capture : move.IsSpatial ? Sfx.Move : Sfx.Jump);

        int line = move.ToTimeline;
        if (_md.Timelines.Count() > count)
            line = _md.Mover == PieceColor.White ? _md.MaxTimeline : _md.MinTimeline;
        if (focus || !BoardVisible(line, move.ToPly + 1))
            FocusOn(line, move.ToPly + 1, 0.5f);
    }

    private void UndoFiveDMove()
    {
        if (_md.TurnMoves.Count == 0)
            return;
        _md.UndoMove();
        _mdSelected = null;
        _mdTargets.Clear();
        RefreshFiveD();
        Sfx.Play(Sfx.Move);
    }

    private void SubmitFiveD()
    {
        if (!_mdCanSubmit)
            return;
        if (_opponent == Opponent.Online)
        {
            // The whole turn goes to the server at once and ends when it comes back.
            _mdSent = string.Join(";", _md.TurnMoves.Select(m => m.ToString()));
            SendAction(_mdSent);
            return;
        }
        _md.Submit();
        AfterFiveDTurn();
    }

    /// <summary>A turn from the server, made of moves separated by ';'.</summary>
    private void ApplyFiveDTurn(string action)
    {
        bool mine = _md.Mover == _human && action == _mdSent && _md.TurnMoves.Count > 0;
        if (!mine)
        {
            while (_md.TurnMoves.Count > 0)
                _md.UndoMove();
            foreach (var part in action.Split(';'))
            {
                if (!_md.TryPlay(part))
                {
                    ShowToast("Ход с сервера не подошёл к доскам: " + action);
                    RefreshFiveD();
                    return;
                }
            }
            var last = _md.TurnMoves.LastOrDefault();
            if (_md.TurnMoves.Count > 0)
                Sfx.Play(last.IsSpatial ? Sfx.Move : Sfx.Jump);
        }
        if (!_md.CanSubmit)
        {
            ShowToast("Ход с сервера не подошёл к доскам: " + action);
            RefreshFiveD();
            return;
        }
        _mdSent = "";
        _md.Submit();
        AfterFiveDTurn();
    }

    private void AfterFiveDTurn()
    {
        _mdSelected = null;
        _mdTargets.Clear();
        RefreshFiveD();
        if (_md.IsOver)
        {
            _endAt = Time.time + 0.9f;
            return;
        }
        if (AnyBoardInCheck())
            Sfx.Play(Sfx.Check);
        MaybeStartFiveDBot();
        FocusPresent();
    }

    private void RematchFiveD()
    {
        if (_opponent == Opponent.Bot)
            _human = ChosenColor();
        StartFiveD();
    }

    // ---------------------------------------------------------------- highlights

    private void RefreshFiveD()
    {
        _mdCanSubmit = _md.CanSubmit;
        _mdPresentMoved = _md.PresentMoved;
        _mdMarks.Clear();
        if (_md.History.Count > 0)
            foreach (var move in _md.History[_md.History.Count - 1])
                MarkMove(move, LastMoveTint);
        foreach (var move in _md.TurnMoves)
            MarkMove(move, TurnTint);
        foreach (var line in _md.Timelines)
        {
            if (!_md.IsActive(line.Index))
                continue;
            var board = line.End;
            var king = KingSquare(board, board.SideToMove);
            if (king.HasValue && board.IsInCheck(board.SideToMove))
                MarkSquare(line.Index, line.EndPly, king.Value, CheckTint);
        }
    }

    private bool AnyBoardInCheck() => _md.Timelines.Any(t => _md.IsActive(t.Index) && t.End.IsInCheck(t.End.SideToMove));

    private static Square? KingSquare(Position board, PieceColor color)
    {
        for (int i = 0; i < 64; i++)
        {
            var piece = board[i];
            if (piece.HasValue && piece.Value.Type == PieceType.King && piece.Value.Color == color)
                return new Square(i);
        }
        return null;
    }

    private void MarkSquare(int timeline, int ply, Square square, Color color)
    {
        if (!_mdMarks.TryGetValue((timeline, ply), out var list))
            _mdMarks[(timeline, ply)] = list = new List<(Square, Color)>();
        list.Add((square, color));
    }

    /// <summary>Marks where a move left from and where it arrived, on the boards it created.</summary>
    private void MarkMove(Move5D move, Color color)
    {
        if (_md.HasTimeline(move.FromTimeline) && _md.Timeline(move.FromTimeline).HasBoard(move.FromPly + 1))
            MarkSquare(move.FromTimeline, move.FromPly + 1, move.From, color);
        if (move.IsSpatial)
        {
            MarkSquare(move.FromTimeline, move.FromPly + 1, move.To, color);
            return;
        }
        int ply = move.ToPly + 1;
        // A jump into the past arrives on the first board of a branch; otherwise on the next board of the target line.
        var branch = _md.Timelines.FirstOrDefault(t => t.Parent == move.ToTimeline && t.StartPly == ply && t[ply][move.To].HasValue);
        if (branch != null)
            MarkSquare(branch.Index, ply, move.To, color);
        else if (_md.HasTimeline(move.ToTimeline) && _md.Timeline(move.ToTimeline).HasBoard(ply))
            MarkSquare(move.ToTimeline, ply, move.To, color);
    }

    // ---------------------------------------------------------------- canvas geometry

    private Rect BoardRect(int timeline, int ply) =>
        new Rect(_mdPan.x + ply * MdColumn * _mdZoom, _mdPan.y + timeline * MdRow * _mdZoom, MdBoard * _mdZoom, MdBoard * _mdZoom);

    private Rect SquareRect(Rect board, Square square)
    {
        float cell = board.width / 8;
        int column = FlipFiveD ? 7 - square.File : square.File;
        int row = FlipFiveD ? square.Rank : 7 - square.Rank;
        return new Rect(board.x + column * cell, board.y + row * cell, cell, cell);
    }

    private bool BoardVisible(int timeline, int ply)
    {
        var rect = BoardRect(timeline, ply);
        return rect.x >= 0 && rect.y >= 30 && rect.xMax <= _mdCanvas.width && rect.yMax <= _mdCanvas.height;
    }

    /// <summary>Slides the view so that this board sits at <paramref name="across"/> of the width and in the middle.</summary>
    private void FocusOn(int timeline, float ply, float across)
    {
        var center = new Vector2((ply * MdColumn + MdBoard / 2) * _mdZoom, (timeline * MdRow + MdBoard / 2) * _mdZoom);
        _mdFocus = new Vector2(_mdCanvas.width * across, _mdCanvas.height / 2 + 14) - center;
    }

    private void FocusPresent()
    {
        var lines = _md.RequiredTimelines().Select(t => t.Index).ToList();
        if (lines.Count == 0)
            lines = _md.PlayableTimelines().Select(t => t.Index).ToList();
        float row = lines.Count > 0 ? (float)lines.Average() : 0;
        var center = new Vector2((_md.Present * MdColumn + MdBoard / 2) * _mdZoom, (row * MdRow + MdBoard / 2) * _mdZoom);
        _mdFocus = new Vector2(_mdCanvas.width * 0.62f, _mdCanvas.height / 2 + 14) - center;
    }

    private void ZoomAt(Vector2 point, float factor)
    {
        float zoom = Mathf.Clamp(_mdZoom * factor, 0.3f, 2.5f);
        _mdPan = point - (point - _mdPan) * (zoom / _mdZoom);
        _mdZoom = zoom;
        _mdFocus = null;
    }

    private (int Timeline, int Ply, Square Square)? HitBoard(Vector2 point)
    {
        foreach (var line in _md.Timelines)
        {
            for (int ply = line.StartPly; ply <= line.EndPly; ply++)
            {
                var rect = BoardRect(line.Index, ply);
                if (!rect.Contains(point))
                    continue;
                float cell = rect.width / 8;
                int column = Mathf.Clamp((int)((point.x - rect.x) / cell), 0, 7);
                int row = Mathf.Clamp((int)((point.y - rect.y) / cell), 0, 7);
                int file = FlipFiveD ? 7 - column : column;
                int rank = FlipFiveD ? row : 7 - row;
                return (line.Index, ply, new Square(file, rank));
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- input

    private void HandleFiveDInput()
    {
        var e = Event.current;
        var mouse = e.mousePosition;
        var inside = new Rect(0, 0, _mdCanvas.width, _mdCanvas.height).Contains(mouse);
        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0 && inside:
                _mdPress = mouse;
                _mdLastMouse = mouse;
                _mdDragging = false;
                e.Use();
                break;
            case EventType.MouseDrag when _mdPress.HasValue:
                if (!_mdDragging && (mouse - _mdPress.Value).magnitude > 8)
                    _mdDragging = true;
                if (_mdDragging)
                {
                    _mdPan += mouse - _mdLastMouse;
                    _mdFocus = null;
                }
                _mdLastMouse = mouse;
                e.Use();
                break;
            case EventType.MouseUp when _mdPress.HasValue:
                if (!_mdDragging && Time.unscaledTime - Theme.LastClickTime > 0.25f)
                    ClickFiveD(mouse);
                _mdPress = null;
                e.Use();
                break;
            case EventType.ScrollWheel when inside:
                ZoomAt(mouse, e.delta.y > 0 ? 1 / 1.12f : 1.12f);
                e.Use();
                break;
        }
    }

    private void ClickFiveD(Vector2 point)
    {
        var hit = HitBoard(point);
        if (hit == null)
        {
            _mdSelected = null;
            _mdTargets.Clear();
            return;
        }
        var (timeline, ply, square) = hit.Value;

        if (_mdSelected.HasValue)
        {
            Move5D? chosen = null;
            foreach (var move in _mdTargets)
            {
                if (move.ToTimeline != timeline || move.ToPly != ply || move.To != square)
                    continue;
                // Promotion is always to a queen here.
                if (chosen == null || move.Promotion == PieceType.Queen)
                    chosen = move;
            }
            if (chosen.HasValue)
            {
                PlayFiveDMove(chosen.Value, false);
                return;
            }
        }

        var piece = _md.Timeline(timeline)[ply][square];
        if (FiveDPlayerMayMove && _md.IsPlayable(timeline, ply) && piece.HasValue && piece.Value.Color == _md.Mover
            && !(_mdSelected.HasValue && _mdSelected.Value == (timeline, square)))
        {
            _mdSelected = (timeline, square);
            _mdTargets.Clear();
            _mdTargets.AddRange(_md.MovesFrom(timeline, square));
            Sfx.Play(Sfx.Click);
        }
        else
        {
            _mdSelected = null;
            _mdTargets.Clear();
        }
    }

    // ---------------------------------------------------------------- drawing

    private void DrawFiveD(float width, float height)
    {
        Theme.Gradient(new Rect(0, 0, width, height), Theme.BackgroundTop, Theme.BackgroundBottom);
        bool window = (_endShown && !_endHidden) || _mdRules;
        Theme.InputBlocked = window;

        _mdCanvas = new Rect(0, 112, width, height - 112 - 74);
        if (_mdNeedFit && Event.current.type == EventType.Repaint)
        {
            _mdNeedFit = false;
            _mdZoom = Mathf.Clamp(Mathf.Min(_mdCanvas.height * 0.62f, width * 0.4f) / MdBoard, 0.5f, 1.8f);
            FocusPresent();
            _mdPan = _mdFocus!.Value;
            _mdFocus = null;
        }

        GUI.BeginGroup(_mdCanvas);
        if (!window)
            HandleFiveDInput();
        if (Event.current.type == EventType.Repaint)
            DrawMultiverse();
        GUI.EndGroup();

        // Top bar.
        if (Theme.Button(new Rect(12, 12, 118, 44), "‹ Меню", Theme.PanelLight, 18))
        {
            Theme.InputBlocked = false;
            LeaveGame();
            return;
        }
        float right = width - 12;
        if (_opponent == Opponent.Online)
        {
            if (!GameFinished && Theme.Button(new Rect(right - 140, 12, 140, 44), "Сдаться", Theme.Red, 18))
                Resign();
        }
        else if (Theme.Button(new Rect(right - 140, 12, 140, 44), "Заново", Theme.Blue, 18))
        {
            RematchFiveD();
        }
        if (Theme.Button(new Rect(right - 292, 12, 140, 44), "Правила", FiveDColor, 18))
            _mdRules = true;
        if (width >= 760)
            GUI.Label(new Rect(140, 12, width - 444, 44), "5D шахматы", Theme.Label(24, FiveDColor, TextAnchor.MiddleCenter, display: true));

        float statusWidth = Mathf.Min(620, width - 24);
        var status = new Rect((width - statusWidth) / 2, 64, statusWidth, 40);
        bool danger = !GameFinished && _mdPresentMoved && !_mdCanSubmit && _md.TurnMoves.Count > 0;
        Theme.Fill(status, Theme.Panel, 20);
        Theme.Outline(status, danger ? Theme.Red : _mdCanSubmit && FiveDPlayerMayMove ? Theme.Green : Theme.PanelLight, 2, 20);
        GUI.Label(status, FiveDStatus(), Theme.Label(statusWidth < 500 ? 15 : 17, Theme.Text, TextAnchor.MiddleCenter, bold: true));
        DrawFiveDTaken(width, height, status);

        // Bottom bar.
        float by = height - 64;
        float gap = 8;
        float zoomWidth = 46;
        float rest = width - 24 - gap * 4 - zoomWidth * 2;
        float undoWidth = rest * 0.3f, submitWidth = rest * 0.4f, presentWidth = rest * 0.3f;
        float x = 12;
        int font = width < 600 ? 15 : 18;
        bool mayMove = FiveDPlayerMayMove;
        if (Theme.Button(new Rect(x, by, undoWidth, 52), "Отменить ход", Theme.Orange, font, mayMove && _md.TurnMoves.Count > 0))
            UndoFiveDMove();
        x += undoWidth + gap;
        if (Theme.Button(new Rect(x, by, submitWidth, 52), "Завершить ход", Theme.Green, font + 2, mayMove && _mdCanSubmit))
            SubmitFiveD();
        x += submitWidth + gap;
        if (Theme.Button(new Rect(x, by, presentWidth, 52), "К настоящему", Theme.Purple, font))
            FocusPresent();
        x += presentWidth + gap;
        var center = new Vector2(_mdCanvas.width / 2, _mdCanvas.height / 2);
        if (Theme.Button(new Rect(x, by, zoomWidth, 52), "−", Theme.PanelLight, 24))
            ZoomAt(center, 1 / 1.25f);
        x += zoomWidth + gap;
        if (Theme.Button(new Rect(x, by, zoomWidth, 52), "+", Theme.PanelLight, 24))
            ZoomAt(center, 1.25f);

        Theme.InputBlocked = false;
        if (_mdRules)
            DrawFiveDRules(width, height);
        else if (window)
            DrawResult(width, height);
    }

    /// <summary>Pieces each side has taken: beside the status line when there is room, otherwise over the bottom of the canvas.</summary>
    private void DrawFiveDTaken(float width, float height, Rect status)
    {
        var captured = _md.CapturedPieces.ToList();
        var byWhite = captured.Where(p => p.Color == PieceColor.Black).ToList();
        var byBlack = captured.Where(p => p.Color == PieceColor.White).ToList();
        float side = status.x - 24;
        var label = Theme.Label(13, Theme.Muted, bold: true);
        if (side >= 140)
        {
            GUI.Label(new Rect(12, 58, side, 18), "Белые съели", label);
            DrawTaken(new Rect(12, 78, side, 24), TakenIcons(byWhite), Advantage(byWhite, byBlack));
            GUI.Label(new Rect(status.xMax + 12, 58, side, 18), "Чёрные съели", label);
            DrawTaken(new Rect(status.xMax + 12, 78, side, 24), TakenIcons(byBlack), Advantage(byBlack, byWhite));
        }
        else
        {
            var strip = new Rect(0, _mdCanvas.yMax - 32, width, 32);
            Theme.Fill(strip, new Color(0.05f, 0.04f, 0.12f, 0.75f));
            float half = (width - 30) / 2;
            DrawTaken(new Rect(10, strip.y + 4, half, 24), TakenIcons(byWhite), Advantage(byWhite, byBlack));
            DrawTaken(new Rect(20 + half, strip.y + 4, half, 24), TakenIcons(byBlack), Advantage(byBlack, byWhite));
        }
    }

    private string FiveDStatus()
    {
        if (GameFinished)
        {
            string result = ResultTitle();
            return EndReason() + " " + char.ToUpper(result[0]) + result.Substring(1).ToLower();
        }
        if (_waitingForServer)
            return "Отправляем ход...";
        if (_mdBotTask != null)
            return "Бот думает...";
        if (FiveDBotBusy)
            return "Бот ходит...";
        if (_opponent == Opponent.Online && _md.Mover != _human)
            return $"Ходит {_opponentName}...";
        string who = _opponent == Opponent.SameDevice ? $"Ход {ColorName(_md.Mover, true)}" : "Ваш ход";
        if (_mdPresentMoved && _md.TurnMoves.Count > 0)
            return _mdCanSubmit ? who + ": можно завершить ход" : "Король под ударом! Отмените ход";
        if (_mdSelected.HasValue)
            return _mdTargets.Any(m => !m.IsSpatial)
                ? "Фиолетовые точки — ходы в прошлое и на другие линии"
                : "У этой фигуры сейчас нет ходов во времени";
        if (_md.TurnMoves.Count > 0)
            return who + ": походите на остальных досках с рамкой";
        return who + ": ходите на досках с золотой рамкой";
    }

    private Texture2D Checker()
    {
        if (_checker != null)
            return _checker;
        _checker = new Texture2D(8, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int file = 0; file < 8; file++)
            for (int rank = 0; rank < 8; rank++)
                _checker.SetPixel(file, rank, (file + rank) % 2 == 0 ? DarkSquare : LightSquare);
        _checker.Apply();
        return _checker;
    }

    private void DrawMultiverse()
    {
        float z = _mdZoom;
        float board = MdBoard * z;
        var view = new Rect(0, 0, _mdCanvas.width, _mdCanvas.height);
        int present = _md.Present;
        var required = new HashSet<int>(_md.RequiredTimelines().Select(t => t.Index));

        // The present: a purple band through every timeline.
        var presentRect = BoardRect(0, present);
        Theme.Fill(new Rect(presentRect.x - 14 * z, 0, board + 28 * z, view.height), Theme.WithAlpha(Theme.Purple, 0.2f));

        // Timeline threads and branches, under the boards.
        float thread = Mathf.Max(3, 7 * z);
        foreach (var line in _md.Timelines)
        {
            var color = Theme.WithAlpha(line.Index == 0 ? Theme.Muted : line.Index > 0 ? WhiteFrame : FiveDColor, _md.IsActive(line.Index) ? 0.55f : 0.2f);
            var first = BoardRect(line.Index, line.StartPly);
            var last = BoardRect(line.Index, line.EndPly);
            Theme.Fill(new Rect(first.center.x, first.center.y - thread / 2, last.center.x - first.center.x, thread), color);
            if (line.Parent.HasValue)
            {
                var parent = BoardRect(line.Parent.Value, line.StartPly - 1);
                float elbow = first.x - 20 * z;
                Theme.Fill(new Rect(parent.center.x, parent.center.y - thread / 2, elbow - parent.center.x, thread), color);
                float top = Mathf.Min(parent.center.y, first.center.y) - thread / 2;
                Theme.Fill(new Rect(elbow - thread / 2, top, thread, Mathf.Abs(first.center.y - parent.center.y) + thread), color);
                Theme.Fill(new Rect(elbow, first.center.y - thread / 2, first.x - elbow, thread), color);
            }
        }

        // Boards.
        float frame = Mathf.Max(3, 6 * z);
        foreach (var line in _md.Timelines)
        {
            bool active = _md.IsActive(line.Index);
            for (int ply = line.StartPly; ply <= line.EndPly; ply++)
            {
                var rect = BoardRect(line.Index, ply);
                if (!rect.Overlaps(view))
                    continue;
                var position = line[ply];
                GUI.color = new Color(1, 1, 1, active ? 1 : 0.45f);

                var outer = new Rect(rect.x - frame, rect.y - frame, rect.width + frame * 2, rect.height + frame * 2);
                Theme.Fill(outer, position.SideToMove == PieceColor.White ? WhiteFrame : BlackFrame, frame);
                if (_md.IsPlayable(line.Index, ply))
                {
                    float pulse = required.Contains(line.Index) && FiveDPlayerMayMove ? 0.6f + 0.4f * Mathf.Sin(Time.time * 4) : 1;
                    var glow = new Rect(outer.x - frame, outer.y - frame, outer.width + frame * 2, outer.height + frame * 2);
                    Theme.Outline(glow, Theme.WithAlpha(Theme.Gold, pulse), frame * 1.3f, frame * 2);
                }
                GUI.DrawTexture(rect, Checker());

                if (_mdMarks.TryGetValue((line.Index, ply), out var marks))
                    foreach (var (square, color) in marks)
                        Theme.Fill(SquareRect(rect, square), color);
                if (_mdSelected.HasValue && _mdSelected.Value.Timeline == line.Index && ply == line.EndPly)
                    Theme.Fill(SquareRect(rect, _mdSelected.Value.Square), SelectedTint);

                for (int i = 0; i < 64; i++)
                {
                    var piece = position[i];
                    if (piece.HasValue && _pieceTextures.TryGetValue(PieceKey(piece.Value), out var texture))
                        GUI.DrawTexture(SquareRect(rect, new Square(i)), texture);
                }

                foreach (var move in _mdTargets)
                {
                    if (move.ToTimeline != line.Index || move.ToPly != ply)
                        continue;
                    var cell = SquareRect(rect, move.To);
                    var color = move.IsSpatial ? TargetDot : TravelDot;
                    if (position[move.To].HasValue)
                    {
                        Theme.Outline(cell, color, Mathf.Max(2, cell.width * 0.12f), cell.width / 2);
                    }
                    else
                    {
                        float dot = cell.width * 0.34f;
                        Theme.Fill(new Rect(cell.center.x - dot / 2, cell.center.y - dot / 2, dot, dot), color, dot / 2);
                    }
                }
            }

            // Timeline name to the left of its first board.
            var start = BoardRect(line.Index, line.StartPly);
            float badge = Mathf.Max(22, 30 * z);
            var badgeRect = new Rect(start.x - frame - badge - 8 * z, start.center.y - badge / 2, badge, badge);
            if (badgeRect.Overlaps(view))
            {
                Theme.Fill(badgeRect, line.Index == 0 ? Theme.PanelLight : line.Index > 0 ? Theme.Darken(WhiteFrame, 0.3f) : FiveDColor, badge / 2);
                string name = line.Index == 0 ? "0" : line.Index > 0 ? "+" + line.Index : "−" + -line.Index;
                GUI.Label(badgeRect, name, Theme.Label(Mathf.RoundToInt(Mathf.Max(11, 14 * z)), line.Index > 0 ? BlackFrame : Theme.Text, TextAnchor.MiddleCenter, bold: true));
            }
            GUI.color = Color.white;
        }

        // Column titles stay at the top of the canvas.
        Theme.Fill(new Rect(0, 0, view.width, 28), new Color(0.05f, 0.04f, 0.12f, 0.75f));
        int firstPly = Mathf.Max(0, Mathf.FloorToInt(-_mdPan.x / (MdColumn * z)) - 1);
        int lastPly = Mathf.CeilToInt((view.width - _mdPan.x) / (MdColumn * z)) + 1;
        int maxPly = _md.Timelines.Max(t => t.EndPly);
        for (int ply = firstPly; ply <= Mathf.Min(lastPly, maxPly); ply++)
        {
            var rect = BoardRect(0, ply);
            string text = board >= 120 ? $"Ход {ply / 2 + 1} · {(ply % 2 == 0 ? "белые" : "чёрные")}" : $"{ply / 2 + 1}{(ply % 2 == 0 ? "б" : "ч")}";
            GUI.Label(new Rect(rect.x, 2, rect.width, 24), text,
                Theme.Label(14, ply == present ? Theme.Gold : Theme.Muted, TextAnchor.MiddleCenter, bold: ply == present));
        }
    }

    // ---------------------------------------------------------------- setup

    private void DrawFiveDRules(float width, float height)
    {
        Dim(width, height, 0.7f);
        float cardWidth = Mathf.Min(680, width - 24);
        float cardHeight = Mathf.Min(640, height - 40);
        var card = new Rect((width - cardWidth) / 2, (height - cardHeight) / 2, cardWidth, cardHeight);
        Theme.Card(card, Theme.Darken(FiveDColor, 0.7f), 24);
        GUI.Label(new Rect(card.x, card.y + 14, card.width, 44), "Правила 5D шахмат", Theme.Label(28, Theme.Gold, TextAnchor.MiddleCenter, display: true));

        var style = Theme.Label(16, Theme.Text, TextAnchor.UpperLeft, wrap: true);
        style.richText = true;
        var view = new Rect(card.x + 20, card.y + 66, card.width - 40, card.height - 66 - 84);
        float textHeight = style.CalcHeight(new GUIContent(FiveDRules), view.width - 24);
        _mdRulesScroll = GUI.BeginScrollView(view, _mdRulesScroll, new Rect(0, 0, view.width - 24, textHeight));
        GUI.Label(new Rect(0, 0, view.width - 24, textHeight), FiveDRules, style);
        GUI.EndScrollView();

        if (Theme.Button(new Rect(card.x + (card.width - 220) / 2, card.yMax - 72, 220, 52), "Понятно", Theme.Green, 20))
            _mdRules = false;
    }

    private void DrawFiveDSetup(float width, float height)
    {
        Theme.InputBlocked = _mdRules;
        BackButton();
        bool bot = _fiveOpponent == 0;
        float cardWidth = Mathf.Min(580, width - 32);
        float x = (width - cardWidth) / 2;
        float cardHeight = 120 + (bot ? 84 : 0) + 240 + 82;
        float y = Mathf.Max(70, (height - cardHeight) / 2);
        Theme.PanelBox(new Rect(x, y, cardWidth, cardHeight), 24);

        float inner = cardWidth - 48;
        float cx = x + 24;
        float cy = y + 20;
        if (_pieceTextures.TryGetValue("wB", out var icon))
            GUI.DrawTexture(new Rect(cx, cy - 4, 56, 56), icon);
        GUI.Label(new Rect(cx + 66, cy, inner - 66, 48), "5D шахматы", Theme.Label(30, Theme.Text, display: true));
        cy += 66;

        GUI.Label(new Rect(cx, cy, inner, 24), "Соперник", Theme.Label(16, Theme.Muted, bold: true));
        _fiveOpponent = Theme.Pills(new Rect(cx, cy + 26, inner, 44), _fiveOpponent, OpponentNames, FiveDColor);
        cy += 84;
        if (bot)
        {
            GUI.Label(new Rect(cx, cy, inner, 24), "Ваш цвет", Theme.Label(16, Theme.Muted, bold: true));
            _menuColor = Theme.Pills(new Rect(cx, cy + 26, inner, 44), _menuColor, ColorNames, FiveDColor);
            cy += 84;
        }

        var rules = new Rect(cx, cy, inner, 226);
        Theme.Fill(rules, Theme.WithAlpha(FiveDColor, 0.16f), 14);
        Theme.Outline(rules, Theme.WithAlpha(FiveDColor, 0.6f), 2, 14);
        string text =
            "•  Все доски партии видны сразу: слева прошлое, справа будущее, ряды — линии времени.\n" +
            "•  Фигура, которая уже сходила, может прыгнуть назад во времени на свою клетку, а ещё ходить в прошлое и между линиями по своим правилам.\n" +
            "•  Ход в прошлое создаёт новую линию времени.\n" +
            "•  Ходите на досках с золотой рамкой, пока фиолетовая полоса «настоящего» не перейдёт к сопернику, и жмите «Завершить ход».\n" +
            "•  Мат — когда любой ваш ход оставляет короля под ударом. Пешки во времени не ходят.";
        GUI.Label(new Rect(rules.x + 14, rules.y + 8, rules.width - 28, rules.height - 16), text,
            Theme.Label(15, Theme.Text, TextAnchor.MiddleLeft, wrap: true));
        cy += 240;

        if (Theme.Button(new Rect(cx, cy, inner / 3 - 6, 62), "Правила", FiveDColor, 20))
            _mdRules = true;
        if (Theme.Button(new Rect(cx + inner / 3 + 6, cy, inner * 2 / 3 - 6, 62), "ИГРАТЬ", Theme.Green, 26))
        {
            SaveMenuSettings();
            if (bot)
            {
                _opponent = Opponent.Bot;
                _human = ChosenColor();
            }
            else
            {
                _opponent = Opponent.SameDevice;
                _human = PieceColor.White;
            }
            StartFiveD();
        }

        Theme.InputBlocked = false;
        if (_mdRules)
            DrawFiveDRules(width, height);
    }
}
