#nullable enable
using System.Collections.Generic;
using System.Linq;
using Chess.Core;
using UnityEngine;

/// <summary>
/// Chess puzzles: mate in one or two. The player always plays the side to move; in mates in two the
/// computer answers with the defence that leaves the fewest mates. Any mating line counts, not only
/// the one stored in the book. The first solve of each puzzle pays coins.
/// </summary>
public partial class ChessBoardView
{
    private static readonly Color PuzzleColor = new Color32(46, 160, 120, 255);

    private Puzzle _puzzle = PuzzleBook.All[0];
    private Position _pzPosition = Position.Start;
    private int _pzMovesLeft;
    private Square? _pzSelected;
    private readonly List<Move> _pzTargets = new List<Move>();
    private Move? _pzLast;
    /// <summary>Every move on the puzzle board so far, sent with the solution so the server can check it.</summary>
    private readonly List<string> _pzLine = new List<string>();
    private Square? _pzWrong;
    private float _pzWrongAt = -100;
    private float _pzInfoAt = -100;
    private float _pzReplyAt = -1;
    private bool _pzSolved;
    private bool _pzHint;
    private string _pzNote = "";
    private Vector2 _pzListScroll;

    // ---------------------------------------------------------------- list

    private void DrawPuzzleList(float width, float height)
    {
        BackButton();

        float cardWidth = Mathf.Min(760, width - 24);
        float x = (width - cardWidth) / 2;
        float y = 76;
        var card = new Rect(x, y, cardWidth, height - y - 16);
        Theme.PanelBox(card, 24);
        if (_pieceTextures.TryGetValue("wK", out var icon))
            GUI.DrawTexture(new Rect(x + 20, y + 12, 52, 52), icon);
        GUI.Label(new Rect(x + 82, y + 14, cardWidth - 100, 44), "Задачи", Theme.Label(30, Theme.Text, display: true));
        int solved = _progress.solved.Count;
        GUI.Label(new Rect(x + 20, y + 14, cardWidth - 40, 44), $"решено {solved} из {PuzzleBook.All.Count}", Theme.Label(16, Theme.Muted, TextAnchor.MiddleRight));
        Theme.Bar(new Rect(x + 20, y + 66, cardWidth - 40, 14), (float)solved / PuzzleBook.All.Count, PuzzleColor);

        var view = new Rect(x + 16, y + 94, cardWidth - 32, card.height - 110);
        float inner = view.width - 20;
        int columns = Mathf.Max(3, (int)(inner / 120));
        float cell = inner / columns;
        float contentHeight = 0;
        foreach (int mate in new[] { 1, 2 })
            contentHeight += 40 + (PuzzleBook.All.Count(p => p.MateIn == mate) + columns - 1) / columns * 84 + 10;

        _pzListScroll = Theme.DragScroll(view, _pzListScroll);

        _pzListScroll = GUI.BeginScrollView(view, _pzListScroll, new Rect(0, 0, inner, contentHeight));
        float cy = 0;
        foreach (int mate in new[] { 1, 2 })
        {
            var group = PuzzleBook.All.Where(p => p.MateIn == mate).ToList();
            var reward = group[0].Reward;
            GUI.Label(new Rect(4, cy, inner, 32), mate == 1 ? "Мат в 1 ход" : "Мат в 2 хода", Theme.Label(20, Theme.Text, bold: true));
            GUI.Label(new Rect(4, cy, inner - 4, 32), $"+{reward} монет за первое решение", Theme.Label(14, Theme.Gold, TextAnchor.MiddleRight));
            cy += 40;
            for (int i = 0; i < group.Count; i++)
            {
                var puzzle = group[i];
                var rect = new Rect(i % columns * cell + 4, cy + i / columns * 84, cell - 8, 76);
                bool done = _progress.solved.Contains(puzzle.Number);
                if (Theme.Button(rect, puzzle.Number.ToString(), done ? Theme.Darken(PuzzleColor, 0.25f) : Theme.PanelLight, 26))
                    StartPuzzle(puzzle);
                string side = puzzle.Start.SideToMove == PieceColor.White ? "белые" : "чёрные";
                GUI.Label(new Rect(rect.x, rect.yMax - 24, rect.width, 20), done ? "решено" : side, Theme.Label(12, done ? Theme.Gold : Theme.Muted, TextAnchor.MiddleCenter));
            }
            cy += (group.Count + columns - 1) / columns * 84 + 10;
        }
        GUI.EndScrollView();
    }

    // ---------------------------------------------------------------- one puzzle

    private void StartPuzzle(Puzzle puzzle)
    {
        _puzzle = puzzle;
        _pzPosition = puzzle.Start;
        _pzMovesLeft = puzzle.MateIn;
        _pzSelected = null;
        _pzTargets.Clear();
        _pzLast = null;
        _pzLine.Clear();
        _pzWrong = null;
        _pzReplyAt = -1;
        _pzSolved = false;
        _pzHint = false;
        _pzNote = "";
        _pzInfoAt = -100;
        ShowPage(Page.Puzzle);
        Sfx.Play(Sfx.Start);
    }

    private void UpdatePuzzle()
    {
        if (_pzReplyAt < 0 || Time.time < _pzReplyAt)
            return;
        _pzReplyAt = -1;
        var defence = MateSolver.BestDefence(_pzPosition, _pzMovesLeft);
        if (!defence.HasValue)
            return;
        bool capture = _pzPosition[defence.Value.To].HasValue;
        _pzPosition = _pzPosition.Apply(defence.Value);
        _pzLine.Add(defence.Value.ToUci());
        _pzLast = defence;
        _pzHint = false;
        Sfx.Play(capture ? Sfx.Capture : Sfx.Move);
    }

    private void PlayPuzzleMove(Square from, Square to)
    {
        // Several moves share a target only for promotions: take one that works, else the queen.
        var candidates = _pzTargets.Where(m => m.From == from && m.To == to).ToList();
        _pzSelected = null;
        _pzTargets.Clear();
        if (candidates.Count == 0)
            return;
        var good = candidates.Where(m => MateSolver.Forces(_pzPosition, m, _pzMovesLeft)).ToList();
        if (good.Count == 0)
        {
            _pzWrong = to;
            _pzWrongAt = Time.time;
            _pzNote = _pzMovesLeft == 1 ? "Это не мат. Попробуй ещё." : "Так соперник спасётся. Попробуй другой ход.";
            Sfx.Play(Sfx.Lose);
            return;
        }

        var move = good[0];
        bool capture = _pzPosition[move.To].HasValue;
        _pzPosition = _pzPosition.Apply(move);
        _pzLine.Add(move.ToUci());
        _pzLast = move;
        _pzHint = false;
        _pzNote = "";
        if (MateSolver.IsCheckmate(_pzPosition))
        {
            _pzSolved = true;
            var before = ClaimableIds();
            bool firstSolve = !_progress.solved.Contains(_puzzle.Number);
            int coins = firstSolve ? Rewards.PuzzleCoins(_progress, _puzzle) : 0;
            Step(new ProgressAction { kind = ProgressAction.Puzzle, number = _puzzle.Number, actions = string.Join("\n", _pzLine) });
            _pzNote = coins > 0 ? $"Мат! Задача решена: +{coins} монет." : firstSolve ? "Мат! Задача решена, но монеты ушли на подсказки." : "Мат! Задача решена.";
            _celebrateAfter = Time.time + 0.6f;
            CelebratePuzzle(_puzzle, coins, firstSolve);
            CelebrateNewQuests(before);
            return;
        }
        _pzMovesLeft--;
        _pzReplyAt = Time.time + 0.7f;
        Sfx.Play(capture ? Sfx.Capture : Sfx.Check);
    }

    private string RewardLine()
    {
        int coins = Rewards.PuzzleCoins(_progress, _puzzle);
        return coins == _puzzle.Reward ? $"Награда: {coins} монет" : $"Награда: {coins} из {_puzzle.Reward} монет";
    }

    private string PuzzleStatus()
    {
        if (_pzSolved || Time.time - _pzWrongAt < 2.5f || Time.time - _pzInfoAt < 2.5f)
            return _pzNote;
        if (_pzReplyAt >= 0)
            return "Верно! Соперник отвечает...";
        string side = _pzPosition.SideToMove == PieceColor.White ? "белых" : "чёрных";
        return _pzMovesLeft == 1 ? $"Ход {side}. Поставь мат в 1 ход." : $"Ход {side}. Поставь мат в {_pzMovesLeft} хода.";
    }

    private void DrawPuzzle(float width, float height)
    {
        Theme.Gradient(new Rect(0, 0, width, height), Theme.BackgroundTop, Theme.BackgroundBottom);
        if (Theme.Button(new Rect(12, 12, 130, 44), "‹ Задачи", Theme.PanelLight, 18))
        {
            ShowPage(Page.Puzzles);
            return;
        }
        float right = width - 12;
        var next = PuzzleBook.All.FirstOrDefault(p => p.Number == _puzzle.Number + 1);
        if (Theme.Button(new Rect(right - 160, 12, 160, 44), "Следующая ›", Theme.Green, 18, next != null))
        {
            StartPuzzle(next!);
            return;
        }
        if (Theme.Button(new Rect(right - 300, 12, 130, 44), "Заново", Theme.Blue, 18))
        {
            StartPuzzle(_puzzle);
            return;
        }
        if (Theme.Button(new Rect(right - 440, 12, 130, 44), "Подсказка", Theme.Orange, 18, !_pzSolved && _pzReplyAt < 0 && !_pzHint))
        {
            // Each hint costs a third of the coins; a hint already on the board is not charged again.
            int before = Rewards.PuzzleCoins(_progress, _puzzle);
            Step(new ProgressAction { kind = ProgressAction.Hint, number = _puzzle.Number });
            _pzHint = true;
            if (!_progress.solved.Contains(_puzzle.Number))
            {
                int now = Rewards.PuzzleCoins(_progress, _puzzle);
                _pzNote = now > 0 ? $"Подсказка: награда {before} → {now} монет." : "Подсказка: за эту задачу монет уже не будет.";
                _pzInfoAt = Time.time;
            }
        }
        if (width >= 860)
            GUI.Label(new Rect(150, 12, width - 600, 44), $"Задача {_puzzle.Number}", Theme.Label(24, PuzzleColor, TextAnchor.MiddleCenter, display: true));

        float statusWidth = Mathf.Min(620, width - 24);
        var status = new Rect((width - statusWidth) / 2, 64, statusWidth, 40);
        bool wrong = !_pzSolved && Time.time - _pzWrongAt < 2.5f;
        Theme.Fill(status, Theme.Panel, 20);
        Theme.Outline(status, _pzSolved ? Theme.Gold : wrong ? Theme.Red : PuzzleColor, 2, 20);
        GUI.Label(status, PuzzleStatus(), Theme.Label(statusWidth < 500 ? 15 : 17, Theme.Text, TextAnchor.MiddleCenter, bold: true));

        var board = PartyBoardRect(width, height, 8, out var panel);
        bool flip = _puzzle.Start.SideToMove == PieceColor.Black;
        float cell = board.width / 8;
        Rect CellRect(Square s) => new Rect(board.x + (flip ? 7 - s.File : s.File) * cell, board.y + (flip ? s.Rank : 7 - s.Rank) * cell, cell, cell);

        var attacker = _puzzle.Start.SideToMove;
        bool mayMove = !_pzSolved && _pzReplyAt < 0 && _pzPosition.SideToMove == attacker;
        if (PartyClick(board, out var point))
        {
            int column = Mathf.Clamp((int)((point.x - board.x) / cell), 0, 7), row = Mathf.Clamp((int)((point.y - board.y) / cell), 0, 7);
            var square = new Square(flip ? 7 - column : column, flip ? row : 7 - row);
            if (_pzSelected.HasValue && _pzTargets.Any(m => m.To == square))
            {
                PlayPuzzleMove(_pzSelected.Value, square);
            }
            else if (mayMove && _pzPosition[square]?.Color == attacker && _pzSelected != square)
            {
                _pzSelected = square;
                _pzTargets.Clear();
                _pzTargets.AddRange(MoveGenerator.GenerateLegal(_pzPosition).Where(m => m.From == square));
                Sfx.Play(Sfx.Click);
            }
            else
            {
                _pzSelected = null;
                _pzTargets.Clear();
            }
        }

        Theme.Fill(new Rect(board.x - 8, board.y - 8, board.width + 16, board.height + 16), Theme.Panel, 12);
        GUI.DrawTexture(board, Checker());
        if (_pzLast.HasValue)
        {
            Theme.Fill(CellRect(_pzLast.Value.From), LastMoveTint);
            Theme.Fill(CellRect(_pzLast.Value.To), LastMoveTint);
        }
        if (_pzPosition.IsInCheck(_pzPosition.SideToMove))
        {
            var king = _pzPosition.FindKing(_pzPosition.SideToMove);
            if (king.HasValue)
                Theme.Fill(CellRect(king.Value), CheckTint);
        }
        if (wrong && _pzWrong.HasValue)
            Theme.Fill(CellRect(_pzWrong.Value), Theme.WithAlpha(Theme.Red, 0.55f));
        if (_pzHint && mayMove)
        {
            // The hint shows which piece to move, not where.
            var hint = MateSolver.MatingMoves(_pzPosition, _pzMovesLeft).FirstOrDefault();
            float pulse = 0.35f + Mathf.Sin(Time.time * 5) * 0.15f;
            Theme.Fill(CellRect(hint.From), Theme.WithAlpha(Theme.Gold, pulse));
        }
        if (_pzSelected.HasValue)
            Theme.Fill(CellRect(_pzSelected.Value), SelectedTint);

        for (int i = 0; i < 64; i++)
        {
            var piece = _pzPosition[i];
            if (piece.HasValue && _pieceTextures.TryGetValue(PieceKey(piece.Value), out var texture))
                GUI.DrawTexture(CellRect(new Square(i)), texture);
        }
        foreach (var move in _pzTargets)
            DrawDot(CellRect(move.To), _pzPosition[move.To].HasValue, TargetDot);

        if (panel.width > 0)
        {
            Theme.PanelBox(panel, 18);
            GUI.Label(new Rect(panel.x + 16, panel.y + 12, panel.width - 32, 30), $"Задача {_puzzle.Number}", Theme.Label(22, Theme.Text, bold: true));
            GUI.Label(new Rect(panel.x + 16, panel.y + 46, panel.width - 32, 26), _puzzle.MateIn == 1 ? "Мат в 1 ход" : "Мат в 2 хода", Theme.Label(17, PuzzleColor, bold: true));
            bool done = _progress.solved.Contains(_puzzle.Number);
            GUI.Label(new Rect(panel.x + 16, panel.y + 78, panel.width - 32, 24), done ? "Уже решена" : RewardLine(),
                Theme.Label(15, done ? Theme.Muted : Theme.Gold));
            GUI.Label(new Rect(panel.x + 16, panel.y + 116, panel.width - 32, 160),
                "Ходи за ту сторону, чья очередь. Подойдёт любой ход, который ставит мат. В задачах на 2 хода соперник ответит сам.\n\n«Подсказка» покажет, какой фигурой ходить. Каждая подсказка забирает треть награды.",
                Theme.Label(14, Theme.Muted, TextAnchor.UpperLeft, wrap: true));
            GUI.Label(new Rect(panel.x + 16, panel.yMax - 40, panel.width - 32, 26), $"Решено: {_progress.solved.Count} из {PuzzleBook.All.Count}",
                Theme.Label(15, Theme.Text, bold: true));
        }
    }
}
