using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Chess.Core
{
    public enum GameStatus
    {
        Ongoing,
        Checkmate,
        Stalemate,
        FiftyMoveRule,
        ThreefoldRepetition,
        InsufficientMaterial,
    }

    /// <summary>A game in progress: validates moves, keeps history and detects how the game ended.</summary>
    public sealed class Game
    {
        private readonly List<Position> _positions = new List<Position>();
        private readonly List<Move> _moves = new List<Move>();
        private readonly List<string> _san = new List<string>();
        private readonly Dictionary<string, int> _repetitions = new Dictionary<string, int>();
        private List<Move> _legalMoves;

        public Game() : this(Position.Start)
        {
        }

        public Game(Position start)
        {
            _positions.Add(start);
            _repetitions[start.PlacementKey()] = 1;
            _legalMoves = MoveGenerator.GenerateLegal(start);
            Status = ComputeStatus();
        }

        public Position Current => _positions[_positions.Count - 1];
        public IReadOnlyList<Position> Positions => _positions;
        public IReadOnlyList<Move> Moves => _moves;

        /// <summary>The moves so far in short algebraic notation, e.g. "e4", "Nf3", "O-O".</summary>
        public IReadOnlyList<string> SanMoves => _san;
        public IReadOnlyList<Move> LegalMoves => _legalMoves;
        public GameStatus Status { get; private set; }
        public bool IsOver => Status != GameStatus.Ongoing;

        /// <summary>The winner after checkmate; null while the game goes on or after a draw.</summary>
        public PieceColor? Winner =>
            Status == GameStatus.Checkmate ? Current.SideToMove.Opponent() : (PieceColor?)null;

        public bool IsLegal(Move move) => !IsOver && _legalMoves.Contains(move);

        /// <summary>The piece a move takes on this board, if any (the pawn for en passant).</summary>
        public static Piece? CapturedBy(Position position, Move move)
        {
            if (move.Kind == MoveKind.EnPassant)
                return new Piece(PieceType.Pawn, position.SideToMove.Opponent());
            return position[move.To];
        }

        /// <summary>Pieces taken in the first <paramref name="moveCount"/> moves (all moves when null), in order.</summary>
        public List<Piece> CapturedPieces(int? moveCount = null)
        {
            var captured = new List<Piece>();
            int count = Math.Min(moveCount ?? _moves.Count, _moves.Count);
            for (int i = 0; i < count; i++)
            {
                var piece = CapturedBy(_positions[i], _moves[i]);
                if (piece.HasValue)
                    captured.Add(piece.Value);
            }
            return captured;
        }

        /// <summary>Finds the legal move between two squares, e.g. for a drag on the board.</summary>
        public Move? FindMove(Square from, Square to, PieceType? promotion = null)
        {
            foreach (var move in _legalMoves)
            {
                if (move.From == from && move.To == to && move.Promotion == promotion)
                    return move;
            }
            return null;
        }

        public Move? FindMove(string uci)
        {
            if (uci == null || (uci.Length != 4 && uci.Length != 5))
                return null;
            var from = Square.Parse(uci.Substring(0, 2));
            var to = Square.Parse(uci.Substring(2, 2));
            PieceType? promotion = uci.Length == 5 ? Piece.FromSymbol(uci[4]).Type : (PieceType?)null;
            return FindMove(from, to, promotion);
        }

        public void MakeMove(Move move)
        {
            if (IsOver)
                throw new InvalidOperationException($"The game is over ({Status}).");
            if (!_legalMoves.Contains(move))
                throw new InvalidOperationException($"Illegal move {move} in {Current.ToFen()}.");

            var next = Current.Apply(move);
            _san.Add(Notation.ToSan(Current, move));
            _moves.Add(move);
            _positions.Add(next);
            string key = next.PlacementKey();
            _repetitions[key] = _repetitions.TryGetValue(key, out int seen) ? seen + 1 : 1;
            _legalMoves = MoveGenerator.GenerateLegal(next);
            Status = ComputeStatus();
        }

        public void MakeMove(string uci)
        {
            var move = FindMove(uci) ?? throw new InvalidOperationException($"Illegal move {uci} in {Current.ToFen()}.");
            MakeMove(move);
        }

        /// <summary>Takes back the last move.</summary>
        public void Undo()
        {
            if (_moves.Count == 0)
                throw new InvalidOperationException("No moves to undo.");
            string key = Current.PlacementKey();
            if (--_repetitions[key] == 0)
                _repetitions.Remove(key);
            _moves.RemoveAt(_moves.Count - 1);
            _san.RemoveAt(_san.Count - 1);
            _positions.RemoveAt(_positions.Count - 1);
            _legalMoves = MoveGenerator.GenerateLegal(Current);
            Status = ComputeStatus();
        }

        /// <summary>The moves in a given language, e.g. Russian "Кf3" instead of "Nf3".</summary>
        public List<string> MovesIn(NotationLanguage language)
        {
            var result = new List<string>(_moves.Count);
            for (int i = 0; i < _moves.Count; i++)
                result.Add(Notation.ToSan(_positions[i], _moves[i], language));
            return result;
        }

        /// <summary>
        /// The game as PGN text that chess sites and programs can open. A game that did not
        /// start from the standard position records its start in the FEN tag.
        /// </summary>
        public string ToPgn(string white = "?", string black = "?", string date = "????.??.??")
        {
            var start = _positions[0];
            string result = Status switch
            {
                GameStatus.Ongoing => "*",
                GameStatus.Checkmate => Winner == PieceColor.White ? "1-0" : "0-1",
                _ => "1/2-1/2",
            };

            var sb = new StringBuilder();
            sb.Append("[Event \"Casual game\"]\n");
            sb.Append($"[Date \"{date}\"]\n");
            sb.Append($"[White \"{white}\"]\n");
            sb.Append($"[Black \"{black}\"]\n");
            sb.Append($"[Result \"{result}\"]\n");
            if (start.ToFen() != Position.StartFen)
            {
                sb.Append("[SetUp \"1\"]\n");
                sb.Append($"[FEN \"{start.ToFen()}\"]\n");
            }
            sb.Append('\n');

            int moveNumber = start.FullmoveNumber;
            bool whiteToMove = start.SideToMove == PieceColor.White;
            for (int i = 0; i < _san.Count; i++)
            {
                if (whiteToMove)
                    sb.Append(moveNumber).Append(". ");
                else if (i == 0)
                    sb.Append(moveNumber).Append("... ");
                sb.Append(_san[i]).Append(' ');
                if (!whiteToMove)
                    moveNumber++;
                whiteToMove = !whiteToMove;
            }
            sb.Append(result);
            return sb.ToString();
        }

        private GameStatus ComputeStatus()
        {
            var position = Current;
            if (_legalMoves.Count == 0)
                return position.IsInCheck(position.SideToMove) ? GameStatus.Checkmate : GameStatus.Stalemate;
            if (position.HalfmoveClock >= 100)
                return GameStatus.FiftyMoveRule;
            if (_repetitions[position.PlacementKey()] >= 3)
                return GameStatus.ThreefoldRepetition;
            if (HasInsufficientMaterial(position))
                return GameStatus.InsufficientMaterial;
            return GameStatus.Ongoing;
        }

        /// <summary>King vs king, king + one minor piece vs king, or bishops all on one colour.</summary>
        public static bool HasInsufficientMaterial(Position position)
        {
            var others = new List<(Piece piece, int index)>();
            for (int i = 0; i < 64; i++)
            {
                var piece = position[i];
                if (piece.HasValue && piece.Value.Type != PieceType.King)
                    others.Add((piece.Value, i));
            }

            if (others.Count == 0)
                return true;
            if (others.Count == 1)
                return others[0].piece.Type == PieceType.Knight || others[0].piece.Type == PieceType.Bishop;
            if (others.All(o => o.piece.Type == PieceType.Bishop))
            {
                int squareColor = SquareColor(others[0].index);
                return others.All(o => SquareColor(o.index) == squareColor);
            }
            return false;
        }

        private static int SquareColor(int index) => (index / 8 + index % 8) % 2;
    }
}
