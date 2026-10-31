#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chess.Core;
using UnityEngine;

/// <summary>The in-game interface: top bar, status, side panel, time travel, promotion and the result screen.</summary>
public partial class ChessBoardView
{
    private Vector2 _moveListScroll;
    private int _shownMoveCount;
    private string _notice = "";

    /// <summary>The rules ended the game, on the ordinary board or in 5D.</summary>
    private bool RulesOver => _page == Page.FiveD ? _md.IsOver : _game.IsOver;

    private bool GameFinished => RulesOver || _onlineEnded;

    private PieceColor? Winner => _onlineEnded ? _onlineWinner : _page == Page.FiveD ? _md.Winner : _game.Winner;

    /// <summary>True for a win, false for a loss, null for a draw or a game between two people at one screen.</summary>
    private bool? PlayerWon
    {
        get
        {
            if (!GameFinished || Winner == null || _opponent == Opponent.SameDevice)
                return null;
            return Winner == _human;
        }
    }

    private static int TakenValue(PieceType type) => type == PieceType.King ? 100 : PieceValues.Of(type);

    private List<(Texture2D? Texture, Color Tint, PieceType Type)> TakenIcons(IEnumerable<Piece> pieces) =>
        pieces.Select(p => (PieceTexture(p.Type, p.Color), Color.white, p.Type)).ToList();

    /// <summary>The material one side has won over the other, as "+3", or empty when it is not ahead.</summary>
    private static string Advantage(IEnumerable<Piece> taken, IEnumerable<Piece> lost)
    {
        int lead = taken.Sum(p => TakenValue(p.Type)) - lost.Sum(p => TakenValue(p.Type));
        return lead > 0 ? "+" + lead : "";
    }

    /// <summary>
    /// Small icons of the pieces a player has taken, the most valuable first, overlapping when there
    /// are many. <paramref name="extra"/> (such as "+3") goes on the right.
    /// </summary>
    private static void DrawTaken(Rect area, List<(Texture2D? Texture, Color Tint, PieceType Type)> pieces, string extra = "")
    {
        var list = pieces.Where(p => p.Texture != null).OrderByDescending(p => TakenValue(p.Type)).ToList();
        float size = area.height;
        float textWidth = extra.Length > 0 ? 40 : 0;
        float room = area.width - textWidth;
        if (list.Count == 0)
        {
            GUI.Label(area, "ничего не съели", Theme.Label(Mathf.RoundToInt(Mathf.Clamp(size * 0.6f, 11, 14)), Theme.Muted));
        }
        else
        {
            float step = list.Count == 1 ? size : Mathf.Min(size * 0.82f, (room - size) / (list.Count - 1));
            var color = GUI.color;
            for (int i = 0; i < list.Count; i++)
            {
                GUI.color = list[i].Tint * color;
                GUI.DrawTexture(new Rect(area.x + i * step, area.y, size, size), list[i].Texture);
            }
            GUI.color = color;
        }
        if (extra.Length > 0)
            GUI.Label(new Rect(area.xMax - textWidth, area.y, textWidth, size), extra, Theme.Label(15, Theme.Gold, TextAnchor.MiddleRight, bold: true));
    }

    private static string ColorName(PieceColor color, bool genitive) =>
        color == PieceColor.White ? (genitive ? "белых" : "белые") : (genitive ? "чёрных" : "чёрные");

    private string PlayerName(PieceColor color)
    {
        switch (_opponent)
        {
            case Opponent.Bot:
                return color == _human ? "Вы" : "Бот";
            case Opponent.Online:
                return color == _human ? _playerName : _opponentName;
            default:
                return color == PieceColor.White ? "Белые" : "Чёрные";
        }
    }

    private static string PieceAccusative(PieceType type) => type switch
    {
        PieceType.Queen => "ферзя",
        PieceType.Rook => "ладью",
        PieceType.Bishop => "слона",
        PieceType.Knight => "коня",
        _ => "пешку",
    };

    private string EndReason()
    {
        if (_onlineEnded && !RulesOver)
        {
            return _onlineReason switch
            {
                "resign" => Winner == _human ? (_opponent == Opponent.Bot ? "Бот сдался." : "Соперник сдался.") : "Вы сдались.",
                "opponent_left" => "Соперник вышел из игры.",
                "connection" => "Связь с сервером пропала.",
                _ => "Партия окончена.",
            };
        }
        if (_page == Page.FiveD)
            return _md.Result == MultiverseResult.Checkmate ? "Мат во всех линиях времени." : "Пат.";
        return _game.Status switch
        {
            GameStatus.Checkmate => "Мат.",
            GameStatus.Stalemate => "Пат.",
            GameStatus.FiftyMoveRule => "Правило 50 ходов.",
            GameStatus.ThreefoldRepetition => "Позиция повторилась три раза.",
            GameStatus.InsufficientMaterial => "Никто не может поставить мат.",
            _ => "",
        };
    }

    private string ResultTitle()
    {
        if (_onlineEnded && _onlineReason == "connection")
            return "СВЯЗЬ ПОТЕРЯНА";
        if (Winner == null)
            return "НИЧЬЯ";
        if (_opponent == Opponent.SameDevice)
            return Winner == PieceColor.White ? "ПОБЕДИЛИ БЕЛЫЕ" : "ПОБЕДИЛИ ЧЁРНЫЕ";
        return PlayerWon == true ? "ПОБЕДА!" : "ПОРАЖЕНИЕ";
    }

    private string StatusText()
    {
        if (GameFinished)
        {
            string result = ResultTitle();
            return EndReason() + " " + char.ToUpper(result[0]) + result.Substring(1).ToLower();
        }
        if (_waitingForServer)
            return "Отправляем ход...";
        if (BotThinking)
            return "Бот думает...";
        if (_jumpMode)
            return "Выберите фигуру для прыжка во времени";
        var side = _game.Current.SideToMove;
        if (_opponent == Opponent.Online && side != _human)
            return $"Ходит {_opponentName}...";
        bool check = _game.Current.IsInCheck(side);
        string turn = _opponent == Opponent.SameDevice ? $"Ход {ColorName(side, true)}" : "Ваш ход";
        return check ? "Шах! " + turn : turn;
    }

    private void LeaveGame()
    {
        if (_opponent == Opponent.Online && !GameFinished)
            Resign();
        ShowPage(_opponent == Opponent.Online ? Page.Online : Page.Home);
    }

    // ---------------------------------------------------------------- layout

    private void DrawGameUi(float width, float height, float scale)
    {
        bool window = _jumpPiece.HasValue || _promotionFrom.HasValue || (_endShown && !_endHidden);
        Theme.InputBlocked = window;

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
        else
        {
            bool undo = Theme.Button(new Rect(right - 140, 12, 140, 44), "Отменить", Theme.Orange, 18, CanUndo);
            bool again = Theme.Button(new Rect(right - 292, 12, 140, 44), "Заново", Theme.Blue, 18);
            if (undo)
                Undo();
            if (again)
                Rematch();
        }

        // Status line.
        float statusWidth = Mathf.Min(520, width - 24);
        var status = new Rect((width - statusWidth) / 2, 64, statusWidth, 42);
        bool check = !GameFinished && _game.Current.IsInCheck(_game.Current.SideToMove);
        Theme.Fill(status, Theme.Panel, 21);
        Theme.Outline(status, check ? Theme.Red : _jumpMode ? Theme.Purple : Theme.PanelLight, 2, 21);
        GUI.Label(status, StatusText(), Theme.Label(19, Theme.Text, TextAnchor.MiddleCenter, bold: true));

        DrawCoordinates(scale);

        if (WideLayout)
            DrawSidePanel(width, scale);
        else
            DrawNarrowExtras(width, scale);

        Theme.InputBlocked = false;
        if (_jumpPiece.HasValue)
            DrawJumpPicker(width, height);
        if (_promotionFrom.HasValue)
            DrawPromotionPicker(width, height);
        DrawJumpFlash(width, height);
        if (_endShown && !_endHidden)
            DrawResult(width, height);
    }

    private Vector2 ToGui(Vector3 world, float scale)
    {
        var point = _camera.WorldToScreenPoint(world);
        return new Vector2(point.x / scale, (Screen.height - point.y) / scale);
    }

    /// <summary>File letters and rank numbers on the board frame.</summary>
    private void DrawCoordinates(float scale)
    {
        var style = Theme.Label(12, new Color32(240, 220, 190, 220), TextAnchor.MiddleCenter, bold: true);
        for (int i = 0; i < 8; i++)
        {
            var file = new Square(i, 0);
            var bottom = SquareToWorld(file);
            float x = bottom.x;
            var letter = ToGui(new Vector3(x, -4.18f, 0), scale);
            GUI.Label(new Rect(letter.x - 10, letter.y - 9, 20, 18), ((char)('a' + i)).ToString(), style);

            var rank = SquareToWorld(new Square(0, i));
            var number = ToGui(new Vector3(-4.18f, rank.y, 0), scale);
            GUI.Label(new Rect(number.x - 10, number.y - 9, 20, 18), (i + 1).ToString(), style);
        }
    }

    private void DrawSidePanel(float width, float scale)
    {
        var topRight = ToGui(new Vector3(4.35f, 4.35f, 0), scale);
        var bottomRight = ToGui(new Vector3(4.35f, -4.35f, 0), scale);
        float left = topRight.x + 18;
        float top = topRight.y;
        float panelWidth = Mathf.Min(300, width - left - 12);
        float panelHeight = bottomRight.y - top;
        if (panelWidth < 160)
            return;
        var panel = new Rect(left, top, panelWidth, panelHeight);
        Theme.PanelBox(panel, 18);

        float x = left + 14;
        float inner = panelWidth - 28;
        float y = top + 12;

        // Players: the side to move glows. Under each one, the pieces they have taken.
        var captured = _game.CapturedPieces();
        foreach (var color in new[] { PieceColor.White, PieceColor.Black })
        {
            var row = new Rect(x, y, inner, 34);
            bool toMove = !GameFinished && _game.Current.SideToMove == color;
            Theme.Fill(row, toMove ? Theme.WithAlpha(Theme.Gold, 0.18f) : Theme.WithAlpha(Theme.PanelLight, 0.4f), 10);
            if (toMove)
                Theme.Outline(row, Theme.WithAlpha(Theme.Gold, 0.8f), 2, 10);
            var king = PieceTexture(PieceType.King, color);
            if (king != null)
                GUI.DrawTexture(new Rect(row.x + 4, row.y + 2, 30, 30), king);
            string name = PlayerName(color);
            if (_opponent == Opponent.Bot && color != _human)
                name += " · " + LevelNames[(int)_level].ToLower();
            if (_botArmyName.Length > 0 && _opponent == Opponent.Bot && color != _human)
                name += " · " + _botArmyName;
            GUI.Label(new Rect(row.x + 40, row.y, row.width - 44, row.height), name, Theme.Label(16, Theme.Text, bold: toMove));
            var taken = captured.Where(p => p.Color != color).ToList();
            var lost = captured.Where(p => p.Color == color).ToList();
            DrawTaken(new Rect(row.x + 6, row.yMax + 3, row.width - 10, 22), TakenIcons(taken), Advantage(taken, lost));
            y += 66;
        }
        y += 4;

        if (_timeTravel)
            y = DrawTimeMachine(new Rect(x, y, inner, 0)) + 10;

        // Move list.
        GUI.Label(new Rect(x, y, inner, 24), "Ходы", Theme.Label(17, Theme.Muted, bold: true));
        y += 26;
        float listBottom = panel.yMax - 12 - 40 - (_notice.Length > 0 ? 40 : 0);
        var list = new Rect(x, y, inner, Mathf.Max(30, listBottom - y));
        Theme.Fill(list, new Color(0, 0, 0, 0.2f), 10);
        DrawMoveList(new Rect(list.x + 8, list.y + 4, list.width - 8, list.height - 8));

        float by = panel.yMax - 12 - 40;
        if (_notice.Length > 0)
            GUI.Label(new Rect(x, by - 40, inner, 38), _notice, Theme.Label(13, Theme.Muted, TextAnchor.MiddleLeft, wrap: true));
        if (Theme.Button(new Rect(x, by, inner, 38), "Сохранить партию", Theme.PanelLight, 15, _game.ActionCount > 0))
            SaveGame();
    }

    private void DrawMoveList(Rect area)
    {
        var log = _game.RussianLog;
        if (log.Count != _shownMoveCount)
        {
            // New move: scroll to the end of the list.
            _shownMoveCount = log.Count;
            _moveListScroll.y = float.MaxValue;
        }
        const float rowHeight = 24;
        int rows = (log.Count + 1) / 2;
        var content = new Rect(0, 0, area.width - 16, rows * rowHeight);
        _moveListScroll = Theme.DragScroll(area, _moveListScroll);
        _moveListScroll = GUI.BeginScrollView(area, _moveListScroll, content, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
        var number = Theme.Label(15, Theme.Muted);
        for (int i = 0; i < log.Count; i += 2)
        {
            float y = i / 2 * rowHeight;
            GUI.Label(new Rect(0, y, 36, rowHeight), $"{i / 2 + 1}.", number);
            GUI.Label(new Rect(38, y, 95, rowHeight), log[i], MoveStyle(log[i]));
            if (i + 1 < log.Count)
                GUI.Label(new Rect(134, y, 95, rowHeight), log[i + 1], MoveStyle(log[i + 1]));
        }
        GUI.EndScrollView();
    }

    /// <summary>Jumps in the move list are purple, so they are easy to find.</summary>
    private static GUIStyle MoveStyle(string entry) =>
        Theme.Label(16, entry.Contains("«") ? Theme.Lighten(Theme.Purple, 0.4f) : Theme.Text, bold: entry.Contains("«"));

    /// <summary>Time travel status and the jump button. Returns the bottom edge.</summary>
    private float DrawTimeMachine(Rect area)
    {
        float y = area.y;
        var box = new Rect(area.x, y, area.width, 124);
        Theme.Fill(box, Theme.WithAlpha(Theme.Purple, 0.18f), 12);
        Theme.Outline(box, Theme.WithAlpha(Theme.Purple, 0.7f), 2, 12);
        GUI.Label(new Rect(box.x + 10, y + 6, box.width - 20, 22), "Машина времени", Theme.Label(16, Theme.Lighten(Theme.Purple, 0.5f), bold: true));
        GUI.Label(new Rect(box.x + 10, y + 6, box.width - 20, 22), $"линия {_game.Timelines.Count}",
            Theme.Label(14, Theme.Muted, TextAnchor.MiddleRight));

        // Jumps left: one dot per jump.
        float rowY = y + 32;
        foreach (var color in new[] { PieceColor.White, PieceColor.Black })
        {
            GUI.Label(new Rect(box.x + 10, rowY, 120, 20), PlayerName(color), Theme.Label(14, Theme.Text));
            for (int i = 0; i < TimeTravelGame.JumpsPerPlayer; i++)
            {
                var dot = new Rect(box.x + 120 + i * 20, rowY + 4, 13, 13);
                Theme.Fill(dot, i < _game.JumpsLeft(color) ? Theme.Purple : Theme.WithAlpha(Theme.Muted, 0.3f), 6.5f);
            }
            int wait = _game.MovesUntilJump(color);
            if (_game.JumpsLeft(color) > 0 && wait > 0)
                GUI.Label(new Rect(box.x + 10, rowY, box.width - 20, 20), $"через {wait}", Theme.Label(13, Theme.Muted, TextAnchor.MiddleRight));
            rowY += 22;
        }

        DrawJumpButton(new Rect(box.x + 10, y + 80, box.width - 20, 36), 15);
        return box.yMax;
    }

    private void DrawJumpButton(Rect rect, int size)
    {
        var side = _game.Current.SideToMove;
        bool mine = _opponent == Opponent.SameDevice || side == _human;
        if (_jumpMode)
        {
            if (Theme.Button(rect, "Отменить прыжок", Theme.Grey, size))
            {
                _jumpMode = false;
                _jumpPiece = null;
                RedrawHighlights();
            }
            return;
        }
        bool can = mine && PlayerMayMove && _game.CanJumpNow(side) && _game.LegalJumps().Count > 0;
        string text = "Прыжок во времени";
        if (mine && !GameFinished && _game.JumpsLeft(side) == 0)
            text = "Прыжки кончились";
        else if (mine && !GameFinished && _game.MovesUntilJump(side) > 0)
            text = $"Прыжок через {_game.MovesUntilJump(side)} х.";
        if (Theme.Button(rect, text, Theme.Purple, size, can))
        {
            _jumpMode = true;
            _selected = null;
            RedrawPieces();
            RedrawHighlights();
        }
    }

    /// <summary>On narrow screens: the last moves and time travel under the board.</summary>
    private void DrawNarrowExtras(float width, float scale)
    {
        var below = ToGui(new Vector3(0, -4.4f, 0), scale);
        float y = below.y + 8;
        var line = Theme.Label(16, Theme.Text, TextAnchor.MiddleCenter);
        string text = _notice;
        if (text.Length == 0)
        {
            var log = _game.RussianLog;
            int start = Mathf.Max(0, log.Count - 6);
            if (start % 2 == 1)
                start--;
            var parts = new StringBuilder();
            for (int i = start; i < log.Count; i++)
                parts.Append(i % 2 == 0 ? $"{i / 2 + 1}. {log[i]}  " : log[i] + "   ");
            text = parts.ToString();
        }
        GUI.Label(new Rect(10, y, width - 20, 24), text, line);

        // Taken pieces: White's on the left half, Black's on the right.
        var captured = _game.CapturedPieces();
        float takenY = y + (_timeTravel ? 102 : 30);
        float half = (width - 30) / 2;
        var byWhite = captured.Where(p => p.Color == PieceColor.Black).ToList();
        var byBlack = captured.Where(p => p.Color == PieceColor.White).ToList();
        DrawTaken(new Rect(10, takenY, half, 24), TakenIcons(byWhite), Advantage(byWhite, byBlack));
        DrawTaken(new Rect(20 + half, takenY, half, 24), TakenIcons(byBlack), Advantage(byBlack, byWhite));
        if (_timeTravel)
        {
            var side = _game.Current.SideToMove;
            GUI.Label(new Rect(10, y + 28, width - 20, 22),
                $"Линия {_game.Timelines.Count} · прыжков: белые {_game.JumpsLeft(PieceColor.White)}, чёрные {_game.JumpsLeft(PieceColor.Black)}",
                Theme.Label(14, Theme.Lighten(Theme.Purple, 0.5f), TextAnchor.MiddleCenter));
            DrawJumpButton(new Rect((width - 260) / 2, y + 54, 260, 42), 17);
        }
    }

    // ---------------------------------------------------------------- windows over the board

    private static void Dim(float width, float height, float alpha = 0.6f)
    {
        Theme.Fill(new Rect(0, 0, width, height), new Color(0.03f, 0.02f, 0.08f, alpha));
    }

    private void DrawJumpPicker(float width, float height)
    {
        var from = _jumpPiece!.Value;
        var piece = _game.Current[from];
        if (!piece.HasValue)
        {
            _jumpPiece = null;
            return;
        }
        Dim(width, height);
        float cardWidth = Mathf.Min(680, width - 24);
        float board = Mathf.Min(190, (cardWidth - 4 * 16) / 3);
        float cardHeight = 70 + 26 + board + 54 + 70;
        var card = new Rect((width - cardWidth) / 2, (height - cardHeight) / 2, cardWidth, cardHeight);
        Theme.Card(card, Theme.Darken(Theme.Purple, 0.55f), 24);
        GUI.Label(new Rect(card.x, card.y + 14, card.width, 40),
            $"Куда отправить {PieceAccusative(piece.Value.Type)} с {from}?",
            Theme.Label(cardWidth < 500 ? 20 : 25, Theme.Text, TextAnchor.MiddleCenter, display: true));

        float left = card.x + (cardWidth - 3 * board - 2 * 16) / 2;
        float y = card.y + 70;
        for (int steps = 1; steps <= TimeTravelGame.MaxStepsBack; steps++)
        {
            var jump = new TimeJump(from, steps);
            var past = _game.PastBoard(steps);
            bool legal = _game.IsLegalJump(jump);
            float x = left + (steps - 1) * (board + 16);
            string ago = steps == 1 ? "1 ход назад" : $"{steps} хода назад";
            GUI.Label(new Rect(x, y, board, 22), ago, Theme.Label(15, Theme.Muted, TextAnchor.MiddleCenter, bold: true));
            var rect = new Rect(x, y + 26, board, board);
            if (past != null)
                DrawMiniBoard(rect, past, from, piece.Value, legal);
            else
                Theme.Fill(rect, new Color(0, 0, 0, 0.3f), 8);

            bool hover = legal && rect.Contains(Event.current.mousePosition);
            if (hover)
                Theme.Outline(rect, Theme.Gold, 3, 6);
            var below = new Rect(x, rect.yMax + 10, board, 38);
            if (legal)
            {
                bool clickedBoard = GUI.Button(rect, GUIContent.none, GUIStyle.none);
                if (clickedBoard)
                    Theme.ClickTaken();
                if (Theme.Button(below, "Сюда", Theme.Purple, 17) || clickedBoard)
                {
                    PlayerJump(jump);
                    return;
                }
            }
            else
            {
                string why = past == null ? "Ещё не было" : past[from].HasValue ? "Клетка занята" : "Будет шах";
                GUI.Label(below, why, Theme.Label(15, Theme.Muted, TextAnchor.MiddleCenter));
            }
        }

        if (Theme.Button(new Rect(card.x + (cardWidth - 200) / 2, card.yMax - 62, 200, 44), "Отмена", Theme.Grey, 17))
        {
            _jumpPiece = null;
            RedrawHighlights();
        }
    }

    /// <summary>A small picture of a past board with the travelling piece shown where it would land.</summary>
    private void DrawMiniBoard(Rect rect, Position position, Square ghostSquare, Piece ghost, bool legal)
    {
        float cell = rect.width / 8;
        for (int i = 0; i < 64; i++)
        {
            var square = new Square(i);
            int file = Flipped ? 7 - square.File : square.File;
            int row = Flipped ? square.Rank : 7 - square.Rank;
            var cellRect = new Rect(rect.x + file * cell, rect.y + row * cell, cell, cell);
            DrawSquare(cellRect, square.File, square.Rank);
            var piece = position[i];
            if (square == ghostSquare)
            {
                Theme.Fill(cellRect, legal ? JumpTint : new Color(0.8f, 0.2f, 0.2f, 0.5f));
                var texture = PieceTexture(ghost.Type, ghost.Color);
                if (!piece.HasValue && texture != null)
                {
                    var color = GUI.color;
                    GUI.color = new Color(1, 1, 1, 0.55f + 0.35f * Mathf.Sin(Time.time * 5));
                    GUI.DrawTexture(cellRect, texture);
                    GUI.color = color;
                }
            }
            if (piece.HasValue)
            {
                var texture = PieceTexture(piece.Value.Type, piece.Value.Color);
                if (texture != null)
                    GUI.DrawTexture(cellRect, texture);
            }
        }
        if (!legal)
            Theme.Fill(rect, new Color(0, 0, 0, 0.35f));
    }

    private void DrawPromotionPicker(float width, float height)
    {
        Dim(width, height, 0.45f);
        var color = _game.Current.SideToMove;
        var card = new Rect((width - 380) / 2, (height - 190) / 2, 380, 190);
        Theme.Card(card, Theme.PanelLight, 22);
        GUI.Label(new Rect(card.x, card.y + 12, card.width, 34), "Во что превратить пешку?", Theme.Label(20, Theme.Text, TextAnchor.MiddleCenter, bold: true));
        var types = new[] { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight };
        for (int i = 0; i < types.Length; i++)
        {
            var rect = new Rect(card.x + 22 + i * 86, card.y + 58, 78, 78);
            bool hover = rect.Contains(Event.current.mousePosition);
            Theme.Fill(rect, hover ? Theme.WithAlpha(Theme.Gold, 0.35f) : new Color(0, 0, 0, 0.25f), 14);
            var texture = PieceTexture(types[i], color);
            if (texture != null)
                GUI.DrawTexture(new Rect(rect.x + 4, rect.y + 4, 70, 70), texture);
            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                Theme.ClickTaken();
                Promote(types[i]);
                return;
            }
        }
        if (GUI.Button(new Rect(card.x, card.yMax - 44, card.width, 40), "Отмена", Theme.Label(15, Theme.Muted, TextAnchor.MiddleCenter)))
        {
            _promotionFrom = null;
            _promotionTo = null;
            RedrawPieces();
        }
    }

    private void DrawJumpFlash(float width, float height)
    {
        float age = Time.time - _jumpFlashAt;
        if (age > 1.8f)
            return;
        if (age < 0.6f)
            Theme.Fill(new Rect(0, 0, width, height), new Color(0.6f, 0.35f, 1f, 0.45f * (1 - age / 0.6f)));
        float alpha = Mathf.Clamp01(age < 0.3f ? age / 0.3f : (1.8f - age) / 0.6f);
        var color = GUI.color;
        GUI.color = new Color(1, 1, 1, alpha);
        var style = Theme.Label(width < 520 ? 30 : 44, Theme.Gold, TextAnchor.MiddleCenter, display: true);
        float rise = age * 20;
        Theme.ShadowText(new Rect(0, height * 0.42f - rise, width, 60), "НОВАЯ ЛИНИЯ ВРЕМЕНИ", style, 4);
        GUI.Label(new Rect(0, height * 0.42f + 54 - rise, width, 30), $"Линия {_game.Timelines.Count}",
            Theme.Label(20, Theme.Text, TextAnchor.MiddleCenter, bold: true));
        GUI.color = color;
    }

    private void DrawResult(float width, float height)
    {
        Dim(width, height, 0.55f);
        float appear = Mathf.Clamp01((Time.time - _endAt) / 0.35f);
        float cardWidth = Mathf.Min(460, width - 24);
        float cardHeight = 300;
        float drop = (1 - appear) * 40;
        var card = new Rect((width - cardWidth) / 2, (height - cardHeight) / 2 - drop, cardWidth, cardHeight);
        var won = PlayerWon;
        var accent = won == true || (_opponent == Opponent.SameDevice && Winner != null) ? Theme.Gold
            : won == false ? Theme.Red : Theme.Blue;
        GUI.color = new Color(1, 1, 1, appear);
        Theme.Card(card, Theme.Darken(accent, 0.6f), 26);

        string title = ResultTitle();
        var titleStyle = Theme.Label(title.Length > 12 ? 30 : 44, accent, TextAnchor.MiddleCenter, display: true);
        Theme.ShadowText(new Rect(card.x, card.y + 30, card.width, 60), title, titleStyle, 3);
        GUI.Label(new Rect(card.x + 20, card.y + 96, card.width - 40, 30), EndReason(), Theme.Label(19, Theme.Text, TextAnchor.MiddleCenter));

        float buttonWidth = (cardWidth - 60) / 2;
        float by = card.y + 150;
        if (_opponent == Opponent.Online)
        {
            if (Theme.Button(new Rect(card.x + 20, by, buttonWidth, 54), "Ещё онлайн", Theme.Teal, 18))
                ShowPage(Page.Online);
        }
        else if (Theme.Button(new Rect(card.x + 20, by, buttonWidth, 54), "Ещё раз", Theme.Green, 19))
        {
            Rematch();
        }
        if (Theme.Button(new Rect(card.x + 40 + buttonWidth, by, buttonWidth, 54), "В меню", Theme.Blue, 19))
            ShowPage(Page.Home);
        if (_opponent == Opponent.Online && _ratingNote.Length > 0)
            GUI.Label(new Rect(card.x, card.y + 206, card.width, 24), _ratingNote, Theme.Label(17, Theme.Gold, TextAnchor.MiddleCenter, bold: true));
        if (GUI.Button(new Rect(card.x, card.yMax - 70, card.width, 40), "Посмотреть доску", Theme.Label(16, Theme.Muted, TextAnchor.MiddleCenter, bold: true)))
        {
            Theme.ClickTaken();
            _endHidden = true;
        }
        GUI.color = Color.white;
    }
}
