using System;
using System.Collections.Generic;
using System.Linq;

namespace Chess.Core
{
    /// <summary>
    /// A move in 5D chess: from a square on one board to a square on the same or another board.
    /// A board is named by its timeline and its ply (half-move number, 0 = the first board).
    /// </summary>
    public readonly struct Move5D : IEquatable<Move5D>
    {
        public Move5D(int fromTimeline, int fromPly, Square from, int toTimeline, int toPly, Square to,
            MoveKind kind = MoveKind.Normal, PieceType? promotion = null)
        {
            FromTimeline = fromTimeline;
            FromPly = fromPly;
            From = from;
            ToTimeline = toTimeline;
            ToPly = toPly;
            To = to;
            Kind = kind;
            Promotion = promotion;
        }

        public int FromTimeline { get; }
        public int FromPly { get; }
        public Square From { get; }
        public int ToTimeline { get; }
        public int ToPly { get; }
        public Square To { get; }
        public MoveKind Kind { get; }
        public PieceType? Promotion { get; }

        /// <summary>True for an ordinary move on one board.</summary>
        public bool IsSpatial => FromTimeline == ToTimeline && FromPly == ToPly;

        /// <summary>The same move as a normal chess move; only meaningful when <see cref="IsSpatial"/>.</summary>
        public Move OnBoard => new Move(From, To, Kind, Promotion);

        /// <summary>Text form used by the server, e.g. "0,4,g1>0,2,g3" or "0,6,e7>0,6,e8q".</summary>
        public override string ToString()
        {
            string text = $"{FromTimeline},{FromPly},{From}>{ToTimeline},{ToPly},{To}";
            if (Promotion.HasValue)
                text += new Piece(Promotion.Value, PieceColor.Black).Symbol;
            return text;
        }

        public bool Equals(Move5D other) =>
            FromTimeline == other.FromTimeline && FromPly == other.FromPly && From == other.From
            && ToTimeline == other.ToTimeline && ToPly == other.ToPly && To == other.To
            && Kind == other.Kind && Promotion == other.Promotion;

        public override bool Equals(object? obj) => obj is Move5D other && Equals(other);

        public override int GetHashCode() =>
            (((FromTimeline * 397 ^ FromPly) * 397 ^ From.Index) * 397 ^ ToTimeline * 31 ^ ToPly) * 397 ^ To.Index;
    }

    /// <summary>One timeline: a row of boards, each one ply after the previous.</summary>
    public sealed class Timeline5D
    {
        private readonly List<Position> _boards;

        internal Timeline5D(int index, int startPly, int? parent, Position first)
        {
            Index = index;
            StartPly = startPly;
            Parent = parent;
            _boards = new List<Position> { first };
        }

        private Timeline5D(Timeline5D other)
        {
            Index = other.Index;
            StartPly = other.StartPly;
            Parent = other.Parent;
            _boards = new List<Position>(other._boards);
        }

        /// <summary>0 for the first timeline, 1, 2... for White's new ones, -1, -2... for Black's.</summary>
        public int Index { get; }

        /// <summary>Ply of the first board. A branch starts one ply after the board it split from.</summary>
        public int StartPly { get; }

        /// <summary>The timeline this one split from; null for timeline 0.</summary>
        public int? Parent { get; }

        public int EndPly => StartPly + _boards.Count - 1;

        public IReadOnlyList<Position> Boards => _boards;

        public Position End => _boards[_boards.Count - 1];

        public bool HasBoard(int ply) => ply >= StartPly && ply <= EndPly;

        public Position this[int ply] => _boards[ply - StartPly];

        internal void Add(Position board) => _boards.Add(board);

        internal void RemoveLast() => _boards.RemoveAt(_boards.Count - 1);

        internal Timeline5D Clone() => new Timeline5D(this);
    }

    public enum MultiverseResult
    {
        Ongoing,
        Checkmate,
        Stalemate,
    }

    /// <summary>
    /// 5D chess with multiverse time travel. Every timeline is a row of boards. On a turn the player
    /// moves on boards where it is their move ("playable" boards). Pieces move in four directions:
    /// along the files and ranks as usual, back in time along a timeline (one step = one full turn)
    /// and across timelines. Rook: one direction; bishop: two at once; queen and king: any number;
    /// knight: two steps in one direction and one in another. Pawns move only on their board.
    /// A piece that lands on a board in the past starts a new timeline from that board.
    /// <para>
    /// The present is the earliest last board among the active timelines. The turn may end once the
    /// present has passed to the opponent and no king of the player can be captured on the opponent's
    /// next turn. A player with no such turn is checkmated if their king is attacked, otherwise it is
    /// stalemate. Timelines count as active while their side has made at most one more of them than
    /// the other side.
    /// </para>
    /// </summary>
    public sealed class MultiverseGame
    {
        /// <summary>Upper limit for the searches that look for a legal turn.</summary>
        public const int SearchBudget = 20000;

        private static readonly int[][] SlideDirections = BuildSlides();
        private static readonly int[][] KnightJumps = BuildKnightJumps();

        private readonly Dictionary<int, Timeline5D> _timelines = new Dictionary<int, Timeline5D>();
        private readonly Stack<UndoRecord> _undo = new Stack<UndoRecord>();
        private readonly List<Move5D> _turnMoves = new List<Move5D>();
        private readonly List<List<Move5D>> _history = new List<List<Move5D>>();

        public MultiverseGame() : this(Position.Start)
        {
        }

        public MultiverseGame(Position start)
        {
            _timelines[0] = new Timeline5D(0, 0, null, start);
            Mover = start.SideToMove;
            UpdateResult();
        }

        private MultiverseGame(MultiverseGame other)
        {
            foreach (var pair in other._timelines)
                _timelines[pair.Key] = pair.Value.Clone();
            Mover = other.Mover;
            Result = other.Result;
            Winner = other.Winner;
            _history.AddRange(other._history);
            foreach (var move in other._turnMoves)
                _turnMoves.Add(move);
            // Undo records point at timelines by index, which the copy shares.
            foreach (var record in other._undo.Reverse())
                _undo.Push(record);
        }

        /// <summary>A full independent copy, for a bot thinking on another thread.</summary>
        public MultiverseGame Clone() => new MultiverseGame(this);

        /// <summary>The player whose turn it is. Stays the same until the turn is submitted.</summary>
        public PieceColor Mover { get; private set; }

        public MultiverseResult Result { get; private set; }

        public PieceColor? Winner { get; private set; }

        public bool IsOver => Result != MultiverseResult.Ongoing;

        public IEnumerable<Timeline5D> Timelines => _timelines.Values.OrderBy(t => t.Index);

        public Timeline5D Timeline(int index) => _timelines[index];

        public bool HasTimeline(int index) => _timelines.ContainsKey(index);

        /// <summary>Moves made so far in the current turn.</summary>
        public IReadOnlyList<Move5D> TurnMoves => _turnMoves;

        /// <summary>All submitted turns, oldest first.</summary>
        public IReadOnlyList<IReadOnlyList<Move5D>> History => _history;

        public int MinTimeline => _timelines.Keys.Min();
        public int MaxTimeline => _timelines.Keys.Max();

        /// <summary>A timeline is active unless its side has made two or more timelines more than the other.</summary>
        public bool IsActive(int index)
        {
            int white = MaxTimeline, black = -MinTimeline;
            if (index > 0)
                return index <= black + 1;
            if (index < 0)
                return -index <= white + 1;
            return true;
        }

        /// <summary>The earliest last board among the active timelines.</summary>
        public int Present => _timelines.Values.Where(t => IsActive(t.Index)).Min(t => t.EndPly);

        /// <summary>Whose move it is at the present.</summary>
        public PieceColor PresentSide => SideAt(Present);

        private PieceColor SideAt(int ply)
        {
            foreach (var timeline in _timelines.Values)
                if (timeline.HasBoard(ply))
                    return timeline[ply].SideToMove;
            throw new InvalidOperationException("No board at ply " + ply);
        }

        /// <summary>Last boards of active timelines where the player whose turn it is may move.</summary>
        public IEnumerable<Timeline5D> PlayableTimelines() =>
            Timelines.Where(t => IsActive(t.Index) && t.End.SideToMove == Mover);

        public bool IsPlayable(int timeline, int ply) =>
            !IsOver && _timelines.TryGetValue(timeline, out var t) && IsActive(timeline)
            && t.EndPly == ply && t.End.SideToMove == Mover;

        /// <summary>Timelines the player still has to move on before the present passes to the opponent.</summary>
        public IEnumerable<Timeline5D> RequiredTimelines()
        {
            int present = Present;
            return Timelines.Where(t => IsActive(t.Index) && t.EndPly == present && t.End.SideToMove == Mover);
        }

        // ---------------------------------------------------------------- moves

        /// <summary>Every move the player can make from the last board of this timeline.</summary>
        public List<Move5D> MovesFrom(int timeline)
        {
            var moves = new List<Move5D>();
            if (!_timelines.TryGetValue(timeline, out var line) || !IsPlayable(timeline, line.EndPly))
                return moves;
            int ply = line.EndPly;
            var board = line.End;
            foreach (var move in MoveGenerator.GeneratePseudoLegal(board))
                moves.Add(new Move5D(timeline, ply, move.From, timeline, ply, move.To, move.Kind, move.Promotion));
            for (int i = 0; i < 64; i++)
            {
                var piece = board[i];
                if (piece.HasValue && piece.Value.Color == Mover)
                    AddTravelMoves(timeline, ply, new Square(i), piece.Value, moves);
            }
            return moves;
        }

        public List<Move5D> MovesFrom(int timeline, Square square) =>
            MovesFrom(timeline).Where(m => m.From == square).ToList();

        public bool IsLegalMove(Move5D move) => MovesFrom(move.FromTimeline).Contains(move);

        /// <summary>Finds a move from its text form, as the server receives it.</summary>
        public Move5D? FindMove(string text)
        {
            if (!TryParseParts(text, out int fl, out int fp, out var from, out int tl, out int tp, out var to, out var promotion))
                return null;
            if (fp != (HasTimeline(fl) ? Timeline(fl).EndPly : -1))
                return null;
            foreach (var move in MovesFrom(fl))
                if (move.FromPly == fp && move.From == from && move.ToTimeline == tl && move.ToPly == tp
                    && move.To == to && move.Promotion == promotion)
                    return move;
            return null;
        }

        private static bool TryParseParts(string text, out int fl, out int fp, out Square from,
            out int tl, out int tp, out Square to, out PieceType? promotion)
        {
            fl = fp = tl = tp = 0;
            from = to = default;
            promotion = null;
            var halves = text.Split('>');
            if (halves.Length != 2)
                return false;
            var a = halves[0].Split(',');
            var b = halves[1].Split(',');
            if (a.Length != 3 || b.Length != 3)
                return false;
            if (!int.TryParse(a[0], out fl) || !int.TryParse(a[1], out fp) || !int.TryParse(b[0], out tl) || !int.TryParse(b[1], out tp))
                return false;
            string target = b[2];
            try
            {
                from = Square.Parse(a[2]);
                if (target.Length == 3)
                {
                    promotion = Piece.FromSymbol(char.ToUpperInvariant(target[2])).Type;
                    target = target.Substring(0, 2);
                }
                to = Square.Parse(target);
            }
            catch (FormatException)
            {
                return false;
            }
            return true;
        }

        private void AddTravelMoves(int timeline, int ply, Square from, Piece piece, List<Move5D> moves)
        {
            // Moves that leave the board: the time and timeline parts of the direction are not both zero.
            switch (piece.Type)
            {
                case PieceType.Pawn:
                    return;
                case PieceType.Knight:
                    foreach (var jump in KnightJumps)
                        TryTarget(timeline, ply, from, piece, jump, 1, moves);
                    AddTimeJumps(timeline, ply, from, piece, moves);
                    return;
            }
            if (piece.Type != PieceType.King)
                AddTimeJumps(timeline, ply, from, piece, moves);
            foreach (var direction in SlideDirections)
            {
                int axes = direction.Count(d => d != 0);
                bool fits = piece.Type switch
                {
                    PieceType.Rook => axes == 1,
                    PieceType.Bishop => axes == 2,
                    _ => true,
                };
                if (!fits)
                    continue;
                int limit = piece.Type == PieceType.King ? 1 : 8;
                for (int step = 1; step <= limit; step++)
                {
                    if (!TryTarget(timeline, ply, from, piece, direction, step, moves))
                        break;
                }
            }
        }

        /// <summary>
        /// Adds the move to the square <paramref name="step"/> times along <paramref name="d"/>
        /// (file, rank, turns, timelines). Returns true when a slide may go on past it.
        /// </summary>
        /// <summary>
        /// Our extra rule on top of 5D chess: a piece other than a king or pawn may jump straight to
        /// its own square on any earlier board where the same side was to move, on any timeline, or
        /// to the same moment of another timeline, if that square is free there. It flies over
        /// everything in between. Without it pieces can rarely reach the past, because the piece
        /// itself usually stood on its square back then.
        /// </summary>
        private void AddTimeJumps(int timeline, int ply, Square from, Piece piece, List<Move5D> moves)
        {
            foreach (var line in _timelines.Values)
            {
                for (int toPly = ply; toPly >= line.StartPly; toPly -= 2)
                {
                    if (!line.HasBoard(toPly) || (line.Index == timeline && toPly == ply))
                        continue;
                    if (line[toPly][from].HasValue)
                        continue;
                    var move = new Move5D(timeline, ply, from, line.Index, toPly, from);
                    if (!moves.Contains(move))
                        moves.Add(move);
                }
            }
        }

        private bool TryTarget(int timeline, int ply, Square from, Piece piece, int[] d, int step, List<Move5D> moves)
        {
            int file = from.File + d[0] * step;
            int rank = from.Rank + d[1] * step;
            int toPly = ply + 2 * d[2] * step;
            int toTimeline = timeline + d[3] * step;
            if (!Square.IsOnBoard(file, rank) || !_timelines.TryGetValue(toTimeline, out var target) || !target.HasBoard(toPly))
                return false;
            var to = new Square(file, rank);
            var occupant = target[toPly][to];
            if (occupant.HasValue && occupant.Value.Color == piece.Color)
                return false;
            moves.Add(new Move5D(timeline, ply, from, toTimeline, toPly, to));
            return !occupant.HasValue;
        }

        private static int[][] BuildSlides()
        {
            var list = new List<int[]>();
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int t = -1; t <= 1; t++)
                        for (int l = -1; l <= 1; l++)
                            if (t != 0 || l != 0)
                                list.Add(new[] { x, y, t, l });
            return list.ToArray();
        }

        private static int[][] BuildKnightJumps()
        {
            var list = new List<int[]>();
            for (int a = 0; a < 4; a++)
            {
                for (int b = 0; b < 4; b++)
                {
                    if (a == b)
                        continue;
                    foreach (int sa in new[] { -2, 2 })
                    {
                        foreach (int sb in new[] { -1, 1 })
                        {
                            var d = new int[4];
                            d[a] = sa;
                            d[b] = sb;
                            if (d[2] != 0 || d[3] != 0)
                                list.Add(d);
                        }
                    }
                }
            }
            return list.ToArray();
        }

        // ---------------------------------------------------------------- playing

        /// <summary>Plays a move of the current turn. Throws if it is not legal.</summary>
        public void MakeMove(Move5D move)
        {
            if (!IsLegalMove(move))
                throw new InvalidOperationException($"Illegal move {move}.");
            Apply(move);
            _turnMoves.Add(move);
        }

        private void Apply(Move5D move)
        {
            var source = _timelines[move.FromTimeline];
            var board = source.End;
            if (move.IsSpatial)
            {
                source.Add(board.Apply(move.OnBoard));
                _undo.Push(new UndoRecord(move.FromTimeline, null, null));
                return;
            }

            var piece = board[move.From]!.Value;
            source.Add(WithPiece(board, move.From, null));

            var target = _timelines[move.ToTimeline];
            var arrived = WithPiece(target[move.ToPly], move.To, piece);
            if (move.ToPly == target.EndPly)
            {
                // The piece arrives on another timeline's present board: that board moves on too.
                target.Add(arrived);
                _undo.Push(new UndoRecord(move.FromTimeline, move.ToTimeline, null));
            }
            else
            {
                // The piece arrives in the past: history splits and a new timeline begins.
                int index = Mover == PieceColor.White ? MaxTimeline + 1 : MinTimeline - 1;
                _timelines[index] = new Timeline5D(index, move.ToPly + 1, move.ToTimeline, arrived);
                _undo.Push(new UndoRecord(move.FromTimeline, null, index));
            }
        }

        /// <summary>The board one ply later with a piece taken away or put down, nothing else moved.</summary>
        private static Position WithPiece(Position board, Square square, Piece? piece)
        {
            var squares = new Piece?[64];
            for (int i = 0; i < 64; i++)
                squares[i] = board[i];
            squares[square.Index] = piece;
            var castling = board.Castling & ~CastlingLostAt(square);
            int fullmove = board.SideToMove == PieceColor.Black ? board.FullmoveNumber + 1 : board.FullmoveNumber;
            return new Position(squares, board.SideToMove.Opponent(), castling, null, 0, fullmove);
        }

        private static CastlingRights CastlingLostAt(Square square)
        {
            switch (square.Index)
            {
                case 0: return CastlingRights.WhiteQueenside;
                case 4: return CastlingRights.WhiteKingside | CastlingRights.WhiteQueenside;
                case 7: return CastlingRights.WhiteKingside;
                case 56: return CastlingRights.BlackQueenside;
                case 60: return CastlingRights.BlackKingside | CastlingRights.BlackQueenside;
                case 63: return CastlingRights.BlackKingside;
                default: return CastlingRights.None;
            }
        }

        /// <summary>Takes back the last move of the current turn.</summary>
        public void UndoMove()
        {
            if (_turnMoves.Count == 0)
                throw new InvalidOperationException("No move to take back in this turn.");
            _turnMoves.RemoveAt(_turnMoves.Count - 1);
            Revert();
        }

        private void Revert()
        {
            var record = _undo.Pop();
            _timelines[record.Source].RemoveLast();
            if (record.Appended.HasValue)
                _timelines[record.Appended.Value].RemoveLast();
            if (record.Created.HasValue)
                _timelines.Remove(record.Created.Value);
        }

        /// <summary>True once the present has passed to the opponent.</summary>
        public bool PresentMoved => PresentSide != Mover;

        /// <summary>True when the turn can be submitted: the present moved and no king is left to be taken.</summary>
        public bool CanSubmit => !IsOver && _turnMoves.Count > 0 && PresentMoved && !KingsAttacked(Mover);

        /// <summary>Ends the turn and checks whether the next player has any legal turn.</summary>
        public void Submit()
        {
            if (!CanSubmit)
                throw new InvalidOperationException("The turn cannot be submitted yet.");
            _history.Add(new List<Move5D>(_turnMoves));
            _turnMoves.Clear();
            _undo.Clear();
            Mover = Mover.Opponent();
            UpdateResult();
        }

        /// <summary>Plays a move or "submit" given as text, as the server receives them.</summary>
        public bool TryPlay(string action)
        {
            if (IsOver)
                return false;
            if (action == "submit")
            {
                if (!CanSubmit)
                    return false;
                Submit();
                return true;
            }
            var move = FindMove(action);
            if (!move.HasValue)
                return false;
            MakeMove(move.Value);
            return true;
        }

        private void UpdateResult()
        {
            if (FindLegalTurn(SearchBudget, null) != false)
                return;
            if (AttackedIfPassing())
            {
                Result = MultiverseResult.Checkmate;
                Winner = Mover.Opponent();
            }
            else
            {
                Result = MultiverseResult.Stalemate;
            }
        }

        // ---------------------------------------------------------------- check

        /// <summary>
        /// True when the opponent, moving next, could capture a king of <paramref name="color"/>: from
        /// any board where the opponent is to move, on the same board or through time and timelines.
        /// </summary>
        public bool KingsAttacked(PieceColor color)
        {
            foreach (var line in _timelines.Values)
                if (AttacksFrom(line, color))
                    return true;
            return false;
        }

        /// <summary>
        /// True when the opponent of <paramref name="color"/>, moving next on the last board of this
        /// timeline, could capture one of <paramref name="color"/>'s kings anywhere.
        /// </summary>
        private bool AttacksFrom(Timeline5D line, PieceColor color)
        {
            var enemy = color.Opponent();
            if (!IsActive(line.Index) || line.End.SideToMove != enemy)
                return false;
            var board = line.End;
            for (int i = 0; i < 64; i++)
            {
                var piece = board[i];
                if (piece.HasValue && piece.Value.Type == PieceType.King && piece.Value.Color == color
                    && MoveGenerator.IsSquareAttacked(board, new Square(i), enemy))
                    return true;
            }
            var targets = new List<Move5D>();
            for (int i = 0; i < 64; i++)
            {
                var piece = board[i];
                if (!piece.HasValue || piece.Value.Color != enemy || piece.Value.Type == PieceType.Pawn)
                    continue;
                targets.Clear();
                AddTravelMoves(line.Index, line.EndPly, new Square(i), piece.Value, targets);
                foreach (var target in targets)
                {
                    var hit = _timelines[target.ToTimeline][target.ToPly][target.To];
                    if (hit.HasValue && hit.Value.Type == PieceType.King && hit.Value.Color == color)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// After a move in the search: true if a board the move just finished lets the opponent take
        /// a king. Such a board cannot change again this turn, so no later move can fix it.
        /// </summary>
        private bool LastMoveExposesKing()
        {
            var record = _undo.Peek();
            if (AttacksFrom(_timelines[record.Source], Mover))
                return true;
            if (record.Appended.HasValue && AttacksFrom(_timelines[record.Appended.Value], Mover))
                return true;
            return record.Created.HasValue && AttacksFrom(_timelines[record.Created.Value], Mover);
        }

        /// <summary>Kings that are attacked right now if the player passed: for showing check.</summary>
        public bool AttackedIfPassing()
        {
            var passed = new List<int>();
            foreach (var line in RequiredTimelines().ToList())
            {
                line.Add(WithPassedTurn(line.End));
                passed.Add(line.Index);
            }
            bool attacked = KingsAttacked(Mover);
            foreach (int index in passed)
                _timelines[index].RemoveLast();
            return attacked;
        }

        private static Position WithPassedTurn(Position board)
        {
            var squares = new Piece?[64];
            for (int i = 0; i < 64; i++)
                squares[i] = board[i];
            return new Position(squares, board.SideToMove.Opponent(), board.Castling, null, board.HalfmoveClock, board.FullmoveNumber);
        }

        // ---------------------------------------------------------------- search

        /// <summary>
        /// Looks for a legal way to finish the turn from the current state and returns the moves to
        /// add, or null when there is none or the budget ran out. <paramref name="order"/>, when
        /// given, scores candidate moves; higher scores are tried first.
        /// </summary>
        public List<Move5D>? FindTurn(int budget, Func<MultiverseGame, Move5D, double>? order)
        {
            var found = new List<Move5D>();
            int left = budget;
            return Search(ref left, found, order) == true ? found : null;
        }

        /// <summary>True if a legal turn exists, false if none, null if the search ran out of budget.</summary>
        public bool? FindLegalTurn(int budget, Func<MultiverseGame, Move5D, double>? order)
        {
            int left = budget;
            return Search(ref left, new List<Move5D>(), order);
        }

        private bool? Search(ref int budget, List<Move5D> path, Func<MultiverseGame, Move5D, double>? order)
        {
            if (budget-- <= 0)
                return null;
            if (PresentMoved && (_turnMoves.Count > 0 || path.Count > 0))
                return !KingsAttacked(Mover);
            // Boards that are already the opponent's cannot change any more this turn.
            if (path.Count == 0 && KingsAttacked(Mover))
                return false;

            var required = RequiredTimelines().FirstOrDefault();
            if (required == null)
                return false;
            IEnumerable<Move5D> moves = MovesFrom(required.Index);
            if (order != null)
                moves = moves.Select(m => (m, order(this, m))).OrderByDescending(p => p.Item2).Select(p => p.m).ToList();

            bool unknown = false;
            foreach (var move in moves)
            {
                Apply(move);
                if (LastMoveExposesKing())
                {
                    Revert();
                    continue;
                }
                path.Add(move);
                var result = Search(ref budget, path, order);
                Revert();
                if (result == true)
                    return true;
                path.RemoveAt(path.Count - 1);
                if (result == null)
                {
                    unknown = true;
                    if (budget <= 0)
                        break;
                }
            }
            return unknown ? (bool?)null : false;
        }

        /// <summary>Plays a move without the legality check, for the search. Undo with <see cref="UndoTry"/>.</summary>
        public void TryApply(Move5D move) => Apply(move);

        public void UndoTry() => Revert();

        private readonly struct UndoRecord
        {
            public UndoRecord(int source, int? appended, int? created)
            {
                Source = source;
                Appended = appended;
                Created = created;
            }

            public int Source { get; }
            public int? Appended { get; }
            public int? Created { get; }
        }
    }
}
