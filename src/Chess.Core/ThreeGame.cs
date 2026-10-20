using System;
using System.Collections.Generic;
using System.Linq;

namespace Chess.Core
{
    /// <summary>A piece in the three-player mode: what it is, who commands it and which army it started in.</summary>
    public readonly struct TriPiece : IEquatable<TriPiece>
    {
        public TriPiece(PieceType type, int owner, int army)
        {
            Type = type;
            Owner = owner;
            Army = army;
        }

        public PieceType Type { get; }

        /// <summary>The player who moves this piece now (0, 1 or 2).</summary>
        public int Owner { get; }

        /// <summary>The army the piece started in; it sets the direction pawns walk.</summary>
        public int Army { get; }

        public TriPiece WithOwner(int owner) => new TriPiece(Type, owner, Army);

        public bool Equals(TriPiece other) => Type == other.Type && Owner == other.Owner && Army == other.Army;
        public override bool Equals(object? obj) => obj is TriPiece other && Equals(other);
        public override int GetHashCode() => HashCode.Combine((int)Type, Owner, Army);
    }

    public readonly struct TriMove : IEquatable<TriMove>
    {
        public TriMove(int from, int to)
        {
            From = from;
            To = to;
        }

        /// <summary>Square index: rank * 12 + file.</summary>
        public int From { get; }
        public int To { get; }

        /// <summary>Text form such as "e2-e4" or "b9-c9".</summary>
        public override string ToString() => ThreeGame.SquareName(From) + "-" + ThreeGame.SquareName(To);

        public bool Equals(TriMove other) => From == other.From && To == other.To;
        public override bool Equals(object? obj) => obj is TriMove other && Equals(other);
        public override int GetHashCode() => From * 256 + To;
    }

    /// <summary>
    /// "Тройной трон", our mode for three players on a 12×12 board. White stands at the bottom,
    /// Red on the left and Black on the right; the top edge is empty. Pieces move as in ordinary
    /// chess (pawns walk towards the opposite edge and turn into queens there; no castling and no en
    /// passant). There is no check: a king can simply be captured. Whoever captures a king takes over
    /// that player's whole army ("Вербовка"), and the last king left wins. A player with no moves
    /// skips the turn; 150 moves in a row without a capture or a pawn move end the game in a draw.
    /// </summary>
    public sealed class ThreeGame
    {
        public const int Size = 12;
        public const int Squares = Size * Size;
        public const int PlayerCount = 3;
        public const int QuietLimit = 150;

        private static readonly PieceType[] BackRank =
        {
            PieceType.Rook, PieceType.Knight, PieceType.Bishop, PieceType.Queen,
            PieceType.King, PieceType.Bishop, PieceType.Knight, PieceType.Rook,
        };

        private static readonly (int X, int Y)[] Forward = { (0, 1), (1, 0), (-1, 0) };
        private static readonly (int X, int Y)[] KnightSteps =
            { (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2) };
        private static readonly (int X, int Y)[] Straight = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        private static readonly (int X, int Y)[] Diagonal = { (1, 1), (1, -1), (-1, 1), (-1, -1) };

        private sealed class State
        {
            public TriPiece?[] Board = new TriPiece?[Squares];
            public int Player;
            public bool[] Alive = { true, true, true };
            public int Quiet;
            public int? Winner;
            public bool Draw;
            public TriMove? Move;
            public int? Eliminated;
            public int By;
            public TriPiece? Captured;

            public State Copy() => new State
            {
                Board = (TriPiece?[])Board.Clone(),
                Player = Player,
                Alive = (bool[])Alive.Clone(),
                Quiet = Quiet,
                Winner = Winner,
                Draw = Draw,
            };
        }

        private readonly List<State> _states = new List<State>();

        public ThreeGame()
        {
            var start = new State();
            for (int i = 0; i < 8; i++)
            {
                // White: files c–j at the bottom, pawns on rank 2.
                start.Board[Index(2 + i, 0)] = new TriPiece(BackRank[i], 0, 0);
                start.Board[Index(2 + i, 1)] = new TriPiece(PieceType.Pawn, 0, 0);
                // Red: ranks 5–12 on the left edge, pawns on file b.
                start.Board[Index(0, 4 + i)] = new TriPiece(BackRank[i], 1, 1);
                start.Board[Index(1, 4 + i)] = new TriPiece(PieceType.Pawn, 1, 1);
                // Black: ranks 5–12 on the right edge, pawns on file k.
                start.Board[Index(11, 4 + i)] = new TriPiece(BackRank[i], 2, 2);
                start.Board[Index(10, 4 + i)] = new TriPiece(PieceType.Pawn, 2, 2);
            }
            _states.Add(start);
        }

        /// <summary>A game from any position, for tests and puzzles.</summary>
        public ThreeGame(TriPiece?[] board, int player)
        {
            if (board.Length != Squares)
                throw new ArgumentException("The board needs " + Squares + " squares.", nameof(board));
            var start = new State { Board = (TriPiece?[])board.Clone(), Player = player };
            for (int p = 0; p < PlayerCount; p++)
                start.Alive[p] = board.Any(x => x.HasValue && x.Value.Owner == p && x.Value.Type == PieceType.King);
            _states.Add(start);
        }

        private State Now => _states[_states.Count - 1];

        public static int Index(int file, int rank) => rank * Size + file;
        public static int FileOf(int square) => square % Size;
        public static int RankOf(int square) => square / Size;
        public static bool OnBoard(int file, int rank) => file >= 0 && file < Size && rank >= 0 && rank < Size;

        public static string SquareName(int square) => (char)('a' + FileOf(square)) + (RankOf(square) + 1).ToString();

        public static int? ParseSquare(string text)
        {
            if (text.Length < 2 || text.Length > 3)
                return null;
            int file = text[0] - 'a';
            if (!int.TryParse(text.Substring(1), out int rank))
                return null;
            rank--;
            return OnBoard(file, rank) ? Index(file, rank) : (int?)null;
        }

        public TriPiece? this[int square] => Now.Board[square];

        /// <summary>Whose turn it is.</summary>
        public int Player => Now.Player;

        public bool IsAlive(int player) => Now.Alive[player];
        public int? Winner => Now.Winner;
        public bool IsDraw => Now.Draw;
        public bool IsOver => Now.Winner.HasValue || Now.Draw;
        public int MoveCount => _states.Count - 1;
        public TriMove? LastMove => Now.Move;

        /// <summary>The player knocked out by the last move, if it captured a king.</summary>
        public int? LastEliminated => Now.Eliminated;

        /// <summary>Moves without a capture or a pawn move, towards the draw at <see cref="QuietLimit"/>.</summary>
        public int QuietMoves => Now.Quiet;

        public int? KingSquare(int player)
        {
            // After a takeover a player can hold only their own king: captured kings leave the board.
            for (int i = 0; i < Squares; i++)
            {
                var piece = Now.Board[i];
                if (piece.HasValue && piece.Value.Type == PieceType.King && piece.Value.Owner == player)
                    return i;
            }
            return null;
        }

        /// <summary>Pieces this player has taken, in order, with the owner they had when taken.</summary>
        public List<TriPiece> CapturedBy(int player) =>
            _states.Skip(1).Where(s => s.By == player && s.Captured.HasValue).Select(s => s.Captured!.Value).ToList();

        public IEnumerable<TriMove> History => _states.Skip(1).Where(s => s.Move.HasValue).Select(s => s.Move!.Value);

        // ---------------------------------------------------------------- moves

        public List<TriMove> LegalMoves()
        {
            var moves = new List<TriMove>();
            if (IsOver)
                return moves;
            for (int i = 0; i < Squares; i++)
            {
                var piece = Now.Board[i];
                if (piece.HasValue && piece.Value.Owner == Player)
                    AddMoves(Now.Board, i, piece.Value, moves);
            }
            return moves;
        }

        public List<TriMove> MovesFrom(int square) => LegalMoves().Where(m => m.From == square).ToList();

        public bool IsLegal(TriMove move) => LegalMoves().Contains(move);

        private static void AddMoves(TriPiece?[] board, int from, TriPiece piece, List<TriMove> moves)
        {
            int file = FileOf(from), rank = RankOf(from);
            switch (piece.Type)
            {
                case PieceType.Pawn:
                {
                    var (dx, dy) = Forward[piece.Army];
                    int f1 = file + dx, r1 = rank + dy;
                    if (OnBoard(f1, r1) && !board[Index(f1, r1)].HasValue)
                    {
                        moves.Add(new TriMove(from, Index(f1, r1)));
                        int f2 = f1 + dx, r2 = r1 + dy;
                        if (OnStartLine(piece.Army, file, rank) && OnBoard(f2, r2) && !board[Index(f2, r2)].HasValue)
                            moves.Add(new TriMove(from, Index(f2, r2)));
                    }
                    // Captures: one step forward and one to either side.
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int cf = f1 + (dx == 0 ? side : 0), cr = r1 + (dy == 0 ? side : 0);
                        if (!OnBoard(cf, cr))
                            continue;
                        var target = board[Index(cf, cr)];
                        if (target.HasValue && target.Value.Owner != piece.Owner)
                            moves.Add(new TriMove(from, Index(cf, cr)));
                    }
                    return;
                }
                case PieceType.Knight:
                    foreach (var (x, y) in KnightSteps)
                        AddStep(board, from, piece, file + x, rank + y, moves);
                    return;
                case PieceType.King:
                    foreach (var (x, y) in Straight.Concat(Diagonal))
                        AddStep(board, from, piece, file + x, rank + y, moves);
                    return;
            }
            if (piece.Type != PieceType.Bishop)
                foreach (var direction in Straight)
                    AddSlide(board, from, piece, direction, moves);
            if (piece.Type != PieceType.Rook)
                foreach (var direction in Diagonal)
                    AddSlide(board, from, piece, direction, moves);
        }

        private static bool OnStartLine(int army, int file, int rank) => army switch
        {
            0 => rank == 1,
            1 => file == 1,
            _ => file == Size - 2,
        };

        private static bool PromotesAt(int army, int square) => army switch
        {
            0 => RankOf(square) == Size - 1,
            1 => FileOf(square) == Size - 1,
            _ => FileOf(square) == 0,
        };

        private static void AddStep(TriPiece?[] board, int from, TriPiece piece, int file, int rank, List<TriMove> moves)
        {
            if (!OnBoard(file, rank))
                return;
            var target = board[Index(file, rank)];
            if (!target.HasValue || target.Value.Owner != piece.Owner)
                moves.Add(new TriMove(from, Index(file, rank)));
        }

        private static void AddSlide(TriPiece?[] board, int from, TriPiece piece, (int X, int Y) direction, List<TriMove> moves)
        {
            int file = FileOf(from) + direction.X, rank = RankOf(from) + direction.Y;
            while (OnBoard(file, rank))
            {
                var target = board[Index(file, rank)];
                if (target.HasValue)
                {
                    if (target.Value.Owner != piece.Owner)
                        moves.Add(new TriMove(from, Index(file, rank)));
                    return;
                }
                moves.Add(new TriMove(from, Index(file, rank)));
                file += direction.X;
                rank += direction.Y;
            }
        }

        /// <summary>True when a piece of any player other than <paramref name="player"/> could capture on this square.</summary>
        public bool IsAttacked(int square, int player) => IsAttacked(Now.Board, square, player);

        private static bool IsAttacked(TriPiece?[] board, int square, int player)
        {
            var moves = new List<TriMove>();
            for (int i = 0; i < Squares; i++)
            {
                var piece = board[i];
                if (!piece.HasValue || piece.Value.Owner == player)
                    continue;
                // Pretend the square holds a piece of the player, so that pawns count their captures.
                var copy = board[square];
                board[square] = new TriPiece(PieceType.Pawn, player, player);
                moves.Clear();
                AddMoves(board, i, piece.Value, moves);
                board[square] = copy;
                foreach (var move in moves)
                    if (move.To == square)
                        return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- playing

        public void MakeMove(TriMove move)
        {
            if (!IsLegal(move))
                throw new InvalidOperationException($"Move {move} is not allowed for player {Player}.");
            var next = Now.Copy();
            var board = next.Board;
            var mover = board[move.From]!.Value;
            var captured = board[move.To];

            board[move.From] = null;
            board[move.To] = mover.Type == PieceType.Pawn && PromotesAt(mover.Army, move.To)
                ? new TriPiece(PieceType.Queen, mover.Owner, mover.Army)
                : mover;
            next.Move = move;
            next.By = mover.Owner;
            next.Captured = captured;
            next.Quiet = captured.HasValue || mover.Type == PieceType.Pawn ? 0 : Now.Quiet + 1;

            if (captured.HasValue && captured.Value.Type == PieceType.King)
            {
                // Вербовка: the loser's army now serves the player who took the king.
                int loser = captured.Value.Owner;
                next.Alive[loser] = false;
                next.Eliminated = loser;
                for (int i = 0; i < Squares; i++)
                    if (board[i].HasValue && board[i]!.Value.Owner == loser)
                        board[i] = board[i]!.Value.WithOwner(mover.Owner);
            }

            if (next.Alive.Count(a => a) == 1)
            {
                next.Winner = Array.IndexOf(next.Alive, true);
            }
            else if (next.Quiet >= QuietLimit)
            {
                next.Draw = true;
            }
            else
            {
                next.Player = NextPlayer(next, mover.Owner);
            }
            _states.Add(next);
        }

        /// <summary>
        /// A player leaves the game (gave up or lost the connection in online play): their army is
        /// taken off the board and the others play on. Nobody recruits it, since no king was taken.
        /// </summary>
        public void Resign(int player)
        {
            if (IsOver || player < 0 || player >= PlayerCount || !Now.Alive[player])
                return;
            var next = Now.Copy();
            next.Alive[player] = false;
            next.Eliminated = player;
            next.By = -1;
            for (int i = 0; i < Squares; i++)
                if (next.Board[i].HasValue && next.Board[i]!.Value.Owner == player)
                    next.Board[i] = null;
            if (next.Alive.Count(a => a) == 1)
                next.Winner = Array.IndexOf(next.Alive, true);
            else if (next.Player == player)
                next.Player = NextPlayer(next, player);
            _states.Add(next);
        }

        /// <summary>The next living player in turn who has a move; a player without moves is skipped.</summary>
        private static int NextPlayer(State state, int current)
        {
            var moves = new List<TriMove>();
            for (int step = 1; step <= PlayerCount; step++)
            {
                int player = (current + step) % PlayerCount;
                if (!state.Alive[player])
                    continue;
                moves.Clear();
                for (int i = 0; i < Squares && moves.Count == 0; i++)
                {
                    var piece = state.Board[i];
                    if (piece.HasValue && piece.Value.Owner == player)
                        AddMoves(state.Board, i, piece.Value, moves);
                }
                if (moves.Count > 0)
                    return player;
            }
            state.Draw = true;
            return current;
        }

        public TriMove? FindMove(string text)
        {
            var parts = text.Split('-');
            if (parts.Length != 2)
                return null;
            var from = ParseSquare(parts[0]);
            var to = ParseSquare(parts[1]);
            if (!from.HasValue || !to.HasValue)
                return null;
            var move = new TriMove(from.Value, to.Value);
            return IsLegal(move) ? move : (TriMove?)null;
        }

        public bool TryPlay(string text)
        {
            var move = FindMove(text);
            if (!move.HasValue)
                return false;
            MakeMove(move.Value);
            return true;
        }

        public void Undo()
        {
            if (_states.Count <= 1)
                throw new InvalidOperationException("No moves to undo.");
            _states.RemoveAt(_states.Count - 1);
        }

        /// <summary>A copy of the board for a bot thinking on another thread.</summary>
        public TriPiece?[] BoardCopy() => (TriPiece?[])Now.Board.Clone();

        internal static List<TriMove> MovesOn(TriPiece?[] board, int player)
        {
            var moves = new List<TriMove>();
            for (int i = 0; i < Squares; i++)
            {
                var piece = board[i];
                if (piece.HasValue && piece.Value.Owner == player)
                    AddMoves(board, i, piece.Value, moves);
            }
            return moves;
        }

        internal static bool AttackedOn(TriPiece?[] board, int square, int player) => IsAttacked(board, square, player);

        internal static TriPiece?[] ApplyOn(TriPiece?[] board, TriMove move)
        {
            var next = (TriPiece?[])board.Clone();
            var mover = next[move.From]!.Value;
            var captured = next[move.To];
            next[move.From] = null;
            next[move.To] = mover.Type == PieceType.Pawn && PromotesAt(mover.Army, move.To)
                ? new TriPiece(PieceType.Queen, mover.Owner, mover.Army)
                : mover;
            if (captured.HasValue && captured.Value.Type == PieceType.King)
                for (int i = 0; i < Squares; i++)
                    if (next[i].HasValue && next[i]!.Value.Owner == captured.Value.Owner)
                        next[i] = next[i]!.Value.WithOwner(mover.Owner);
            return next;
        }
    }

    /// <summary>
    /// A simple bot for the three-player mode. It looks one move ahead: grab what is worth grabbing
    /// (a king most of all, since its army comes along), do not leave the moved piece or the own king
    /// where an opponent can take it, push pawns and keep a little randomness.
    /// </summary>
    public sealed class ThreeBot
    {
        private readonly Random _random;

        public ThreeBot(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        private static double Value(PieceType type) => type switch
        {
            PieceType.Pawn => 1,
            PieceType.Knight => 3,
            PieceType.Bishop => 3.2,
            PieceType.Rook => 5,
            PieceType.Queen => 9,
            _ => 60,
        };

        /// <summary>Works on copies only, so it may run on a background thread.</summary>
        public TriMove? Choose(TriPiece?[] board, int player)
        {
            var moves = ThreeGame.MovesOn(board, player);
            if (moves.Count == 0)
                return null;
            TriMove? best = null;
            double bestScore = double.MinValue;
            foreach (var move in moves)
            {
                double score = Score(board, player, move);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = move;
                }
            }
            return best;
        }

        private static int Distance(int a, int b) =>
            Math.Max(Math.Abs(ThreeGame.FileOf(a) - ThreeGame.FileOf(b)), Math.Abs(ThreeGame.RankOf(a) - ThreeGame.RankOf(b)));

        private double Score(TriPiece?[] board, int player, TriMove move)
        {
            var mover = board[move.From]!.Value;
            var captured = board[move.To];
            double score = _random.NextDouble() * 0.6;
            if (captured.HasValue)
            {
                score += Value(captured.Value.Type) * 10;
                if (captured.Value.Type == PieceType.King)
                {
                    // The whole army comes along.
                    for (int i = 0; i < ThreeGame.Squares; i++)
                        if (board[i].HasValue && board[i]!.Value.Owner == captured.Value.Owner)
                            score += Value(board[i]!.Value.Type) * 3;
                }
            }

            var after = ThreeGame.ApplyOn(board, move);
            if (ThreeGame.AttackedOn(after, move.To, player))
                score -= Value(mover.Type) * 9;
            else if (ThreeGame.AttackedOn(board, move.From, player))
                score += Value(mover.Type) * 4; // saved a piece that was under attack

            if (mover.Type != PieceType.King)
            {
                for (int i = 0; i < ThreeGame.Squares; i++)
                {
                    var piece = after[i];
                    if (piece.HasValue && piece.Value.Owner == player && piece.Value.Type == PieceType.King && ThreeGame.AttackedOn(after, i, player))
                        score -= 400;
                }
            }

            if (mover.Type == PieceType.Pawn)
            {
                score += 0.4;
                if (after[move.To]!.Value.Type == PieceType.Queen)
                    score += 70;
            }
            // Hunt kings: a piece that now threatens one is good, and in quiet times step closer to them.
            var threats = ThreeGame.MovesOn(after, player).Where(m => m.From == move.To);
            foreach (var threat in threats)
            {
                var target = after[threat.To];
                if (target.HasValue && target.Value.Type == PieceType.King)
                    score += 6;
            }
            int nearest = int.MaxValue;
            for (int i = 0; i < ThreeGame.Squares; i++)
            {
                var piece = board[i];
                if (piece.HasValue && piece.Value.Type == PieceType.King && piece.Value.Owner != player)
                {
                    int before = Distance(move.From, i), now = Distance(move.To, i);
                    nearest = Math.Min(nearest, now - before);
                }
            }
            if (nearest != int.MaxValue && mover.Type != PieceType.King)
                score -= nearest * 0.25;

            // Prefer the middle of the board a little.
            float centre = (ThreeGame.Size - 1) / 2f;
            float distance = Math.Abs(ThreeGame.FileOf(move.To) - centre) + Math.Abs(ThreeGame.RankOf(move.To) - centre);
            score += (11 - distance) * 0.08;
            return score;
        }
    }
}
