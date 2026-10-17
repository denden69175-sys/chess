using System;
using System.Collections.Generic;

namespace Chess.Core
{
    /// <summary>
    /// A piece sent back in time: the piece on <see cref="From"/> appears on the same square of the
    /// board as it stood <see cref="StepsBack"/> of its owner's moves ago.
    /// </summary>
    public readonly struct TimeJump : IEquatable<TimeJump>
    {
        public TimeJump(Square from, int stepsBack)
        {
            From = from;
            StepsBack = stepsBack;
        }

        public Square From { get; }
        public int StepsBack { get; }

        /// <summary>Text form used by the server protocol, e.g. "jump:e4:2".</summary>
        public override string ToString() => $"jump:{From}:{StepsBack}";

        public static bool TryParse(string text, out TimeJump jump)
        {
            jump = default;
            var parts = text.Split(':');
            if (parts.Length != 3 || parts[0] != "jump" || !int.TryParse(parts[2], out int steps))
                return false;
            try
            {
                jump = new TimeJump(Square.Parse(parts[1]), steps);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public bool Equals(TimeJump other) => From == other.From && StepsBack == other.StepsBack;
        public override bool Equals(object? obj) => obj is TimeJump other && Equals(other);
        public override int GetHashCode() => From.Index * 8 + StepsBack;
    }

    /// <summary>One line of play. A new one starts every time a piece travels back in time.</summary>
    public sealed class Timeline
    {
        internal Timeline(int id, int? parentId, int branchMove, Game game)
        {
            Id = id;
            ParentId = parentId;
            BranchMove = branchMove;
            Game = game;
        }

        public int Id { get; }

        /// <summary>The timeline this one split from; null for the first one.</summary>
        public int? ParentId { get; }

        /// <summary>Index into the parent's positions of the board the piece travelled to.</summary>
        public int BranchMove { get; }

        public Game Game { get; }
    }

    /// <summary>
    /// The time-travel mode, first version: instead of a move, a player may send one of their pieces
    /// (not the king) back to the board of one of their last <see cref="MaxStepsBack"/> moves. The
    /// piece appears there as an extra piece, a new timeline starts from that board and play goes on
    /// in it; the old timeline is frozen. Each player has <see cref="JumpsPerPlayer"/> jumps, at least
    /// <see cref="Cooldown"/> own moves apart. The piece may not land on an occupied square or give
    /// check on arrival. Checkmate in the active timeline ends the game.
    /// </summary>
    public sealed class TimeTravelGame
    {
        public const int JumpsPerPlayer = 2;
        public const int MaxStepsBack = 3;
        public const int Cooldown = 5;

        private readonly List<Timeline> _timelines = new List<Timeline>();
        private readonly List<string> _log = new List<string>();
        private readonly List<string> _russianLog = new List<string>();
        private readonly Stack<UndoRecord> _undo = new Stack<UndoRecord>();
        private readonly int[] _jumpsLeft = { JumpsPerPlayer, JumpsPerPlayer };
        private readonly int[] _ownMoves = { 0, 0 };
        private readonly int[] _lastJumpAt = { -Cooldown, -Cooldown };
        private int _active;

        public TimeTravelGame() : this(Position.Start)
        {
        }

        public TimeTravelGame(Position start)
        {
            _timelines.Add(new Timeline(0, null, 0, new Game(start)));
        }

        public IReadOnlyList<Timeline> Timelines => _timelines;
        public Timeline ActiveTimeline => _timelines[_active];
        public Game Active => _timelines[_active].Game;
        public Position Current => Active.Current;
        public GameStatus Status => Active.Status;
        public bool IsOver => Active.IsOver;
        public PieceColor? Winner => Active.Winner;

        /// <summary>Every action so far, moves and jumps, in English notation.</summary>
        public IReadOnlyList<string> Log => _log;

        /// <summary>The same actions in Russian notation, e.g. "Кf3" or "Кf3 «2" (two own moves back).</summary>
        public IReadOnlyList<string> RussianLog => _russianLog;

        public int ActionCount => _log.Count;

        public int JumpsLeft(PieceColor color) => _jumpsLeft[(int)color];

        /// <summary>Own moves the player still has to make before the next jump is allowed.</summary>
        public int MovesUntilJump(PieceColor color) =>
            Math.Max(0, Cooldown - (_ownMoves[(int)color] - _lastJumpAt[(int)color]));

        public bool CanJumpNow(PieceColor color) =>
            !IsOver && Current.SideToMove == color && JumpsLeft(color) > 0 && MovesUntilJump(color) == 0;

        /// <summary>The board <paramref name="stepsBack"/> of the side to move's own moves ago, if it exists.</summary>
        public Position? PastBoard(int stepsBack)
        {
            int index = Active.Positions.Count - 1 - 2 * stepsBack;
            return stepsBack >= 1 && index >= 0 ? Active.Positions[index] : null;
        }

        public bool IsLegalMove(Move move) => Active.IsLegal(move);

        public void MakeMove(Move move)
        {
            var color = Current.SideToMove;
            string san = Notation.ToSan(Current, move);
            string russian = Notation.ToSan(Current, move, NotationLanguage.Russian);
            Active.MakeMove(move);
            _ownMoves[(int)color]++;
            _log.Add(san);
            _russianLog.Add(russian);
            _undo.Push(new UndoRecord(false, _active, 0));
        }

        /// <summary>All jumps the side to move may make right now.</summary>
        public List<TimeJump> LegalJumps()
        {
            var jumps = new List<TimeJump>();
            var color = Current.SideToMove;
            if (!CanJumpNow(color))
                return jumps;
            for (int i = 0; i < 64; i++)
            {
                var piece = Current[i];
                if (!piece.HasValue || piece.Value.Color != color || piece.Value.Type == PieceType.King)
                    continue;
                for (int steps = 1; steps <= MaxStepsBack; steps++)
                {
                    var jump = new TimeJump(new Square(i), steps);
                    if (ResultOf(jump) != null)
                        jumps.Add(jump);
                }
            }
            return jumps;
        }

        public bool IsLegalJump(TimeJump jump) => CanJumpNow(Current.SideToMove) && ResultOf(jump) != null;

        /// <summary>
        /// The board the game would continue from after this jump, or null when the jump breaks a rule.
        /// Does not check the jump count or the cooldown; see <see cref="IsLegalJump"/>.
        /// </summary>
        public Position? ResultOf(TimeJump jump)
        {
            var color = Current.SideToMove;
            var piece = Current[jump.From];
            if (!piece.HasValue || piece.Value.Color != color || piece.Value.Type == PieceType.King)
                return null;
            if (jump.StepsBack < 1 || jump.StepsBack > MaxStepsBack)
                return null;
            var past = PastBoard(jump.StepsBack);
            if (past == null || past[jump.From].HasValue)
                return null;

            var board = new Piece?[64];
            for (int i = 0; i < 64; i++)
                board[i] = past[i];
            board[jump.From.Index] = piece;

            // The jump uses the player's turn on the old board, so the opponent moves next there.
            var result = new Position(board, color.Opponent(), past.Castling, null, 0, past.FullmoveNumber);
            if (result.IsInCheck(color) || result.IsInCheck(color.Opponent()))
                return null;
            return result;
        }

        public void Jump(TimeJump jump)
        {
            if (!IsLegalJump(jump))
                throw new InvalidOperationException($"Illegal jump {jump}.");
            var color = Current.SideToMove;
            var piece = Current[jump.From]!.Value;
            var start = ResultOf(jump)!;
            int branchMove = Active.Positions.Count - 1 - 2 * jump.StepsBack;

            _undo.Push(new UndoRecord(true, _active, _lastJumpAt[(int)color]));
            _timelines.Add(new Timeline(_timelines.Count, ActiveTimeline.Id, branchMove, new Game(start)));
            _active = _timelines.Count - 1;
            _jumpsLeft[(int)color]--;
            _lastJumpAt[(int)color] = _ownMoves[(int)color];
            _ownMoves[(int)color]++;
            _log.Add($"{Notation.Letter(piece.Type, NotationLanguage.English)}{jump.From}@-{jump.StepsBack}");
            _russianLog.Add($"{Notation.Letter(piece.Type, NotationLanguage.Russian)}{jump.From} «{jump.StepsBack}");
        }

        /// <summary>Plays a move ("e2e4") or a jump ("jump:e4:2") given as text, as the server receives them.</summary>
        public bool TryPlay(string action)
        {
            if (IsOver)
                return false;
            if (TimeJump.TryParse(action, out var jump))
            {
                if (!IsLegalJump(jump))
                    return false;
                Jump(jump);
                return true;
            }
            var move = Active.FindMove(action);
            if (!move.HasValue)
                return false;
            MakeMove(move.Value);
            return true;
        }

        /// <summary>
        /// Everything a bot needs to decide, copied so the search can run on another thread
        /// while the game object stays with the caller.
        /// </summary>
        public TimeTravelChoice SnapshotForBot()
        {
            var jumps = new List<(TimeJump, Position)>();
            foreach (var jump in LegalJumps())
                jumps.Add((jump, ResultOf(jump)!));
            return new TimeTravelChoice(Current, jumps);
        }

        /// <summary>Takes back the last move or jump.</summary>
        public void Undo()
        {
            if (_undo.Count == 0)
                throw new InvalidOperationException("Nothing to undo.");
            var record = _undo.Pop();
            _log.RemoveAt(_log.Count - 1);
            _russianLog.RemoveAt(_russianLog.Count - 1);
            if (record.WasJump)
            {
                var color = _timelines[record.Timeline].Game.Current.SideToMove;
                _timelines.RemoveAt(_timelines.Count - 1);
                _active = record.Timeline;
                _jumpsLeft[(int)color]++;
                _ownMoves[(int)color]--;
                _lastJumpAt[(int)color] = record.PreviousLastJump;
            }
            else
            {
                Active.Undo();
                _ownMoves[(int)Current.SideToMove]--;
            }
        }

        private readonly struct UndoRecord
        {
            public UndoRecord(bool wasJump, int timeline, int previousLastJump)
            {
                WasJump = wasJump;
                Timeline = timeline;
                PreviousLastJump = previousLastJump;
            }

            public bool WasJump { get; }
            public int Timeline { get; }
            public int PreviousLastJump { get; }
        }
    }

    /// <summary>A position plus the jumps available in it, for <see cref="Bot.ChooseTimeTravelAction"/>.</summary>
    public sealed class TimeTravelChoice
    {
        public TimeTravelChoice(Position current, List<(TimeJump Jump, Position Result)> jumps)
        {
            Current = current;
            Jumps = jumps;
        }

        public Position Current { get; }
        public List<(TimeJump Jump, Position Result)> Jumps { get; }
    }
}
