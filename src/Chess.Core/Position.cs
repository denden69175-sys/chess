using System;
using System.Text;

namespace Chess.Core
{
    [Flags]
    public enum CastlingRights
    {
        None = 0,
        WhiteKingside = 1,
        WhiteQueenside = 2,
        BlackKingside = 4,
        BlackQueenside = 8,
        All = WhiteKingside | WhiteQueenside | BlackKingside | BlackQueenside,
    }

    /// <summary>
    /// A snapshot of the board: pieces, side to move, castling rights, en passant square and clocks.
    /// Positions are treated as immutable: <see cref="Apply"/> returns a new position.
    /// </summary>
    public sealed class Position
    {
        public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        private readonly Piece?[] _board;

        public Position(
            Piece?[] board,
            PieceColor sideToMove,
            CastlingRights castling,
            Square? enPassant,
            int halfmoveClock,
            int fullmoveNumber)
        {
            if (board.Length != 64)
                throw new ArgumentException("Board must have 64 squares.", nameof(board));
            _board = (Piece?[])board.Clone();
            SideToMove = sideToMove;
            Castling = castling;
            EnPassant = enPassant;
            HalfmoveClock = halfmoveClock;
            FullmoveNumber = fullmoveNumber;
        }

        public static Position Start => FromFen(StartFen);

        public PieceColor SideToMove { get; }
        public CastlingRights Castling { get; }

        /// <summary>Square a pawn may capture onto en passant, if the last move was a double push.</summary>
        public Square? EnPassant { get; }

        /// <summary>Half-moves since the last capture or pawn move (for the 50-move rule).</summary>
        public int HalfmoveClock { get; }

        public int FullmoveNumber { get; }

        public Piece? this[Square square] => _board[square.Index];
        public Piece? this[int index] => _board[index];

        public Square? FindKing(PieceColor color)
        {
            var king = new Piece(PieceType.King, color);
            for (int i = 0; i < 64; i++)
                if (_board[i] == king)
                    return new Square(i);
            return null;
        }

        public bool IsInCheck(PieceColor color)
        {
            var king = FindKing(color);
            return king.HasValue && MoveGenerator.IsSquareAttacked(this, king.Value, color.Opponent());
        }

        /// <summary>Plays a move without checking that it is legal. Use <see cref="Game"/> for validated play.</summary>
        public Position Apply(Move move)
        {
            var board = (Piece?[])_board.Clone();
            var mover = board[move.From.Index] ?? throw new InvalidOperationException($"No piece on {move.From}.");
            bool capture = board[move.To.Index].HasValue || move.Kind == MoveKind.EnPassant;

            board[move.From.Index] = null;
            board[move.To.Index] = move.Promotion.HasValue ? new Piece(move.Promotion.Value, mover.Color) : mover;

            switch (move.Kind)
            {
                case MoveKind.EnPassant:
                    int capturedIndex = move.To.Index + (mover.Color == PieceColor.White ? -8 : 8);
                    board[capturedIndex] = null;
                    break;
                case MoveKind.CastleKingside:
                    MoveRook(board, move.From.Rank, 7, 5);
                    break;
                case MoveKind.CastleQueenside:
                    MoveRook(board, move.From.Rank, 0, 3);
                    break;
            }

            var castling = Castling & ~RightsLostBy(move.From) & ~RightsLostBy(move.To);

            Square? enPassant = null;
            if (move.Kind == MoveKind.DoublePawnPush)
                enPassant = new Square((move.From.Index + move.To.Index) / 2);

            int halfmove = mover.Type == PieceType.Pawn || capture ? 0 : HalfmoveClock + 1;
            int fullmove = SideToMove == PieceColor.Black ? FullmoveNumber + 1 : FullmoveNumber;

            return new Position(board, SideToMove.Opponent(), castling, enPassant, halfmove, fullmove);
        }

        private static void MoveRook(Piece?[] board, int rank, int fromFile, int toFile)
        {
            int from = rank * 8 + fromFile;
            board[rank * 8 + toFile] = board[from];
            board[from] = null;
        }

        /// <summary>Any move from or to a king/rook home square removes the matching castling right.</summary>
        private static CastlingRights RightsLostBy(Square square)
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

        public static Position FromFen(string fen)
        {
            var parts = fen.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
                throw new FormatException("FEN needs at least 4 fields.");

            var board = new Piece?[64];
            var ranks = parts[0].Split('/');
            if (ranks.Length != 8)
                throw new FormatException("FEN board must have 8 ranks.");
            for (int r = 0; r < 8; r++)
            {
                int rank = 7 - r;
                int file = 0;
                foreach (char c in ranks[r])
                {
                    if (char.IsDigit(c))
                    {
                        file += c - '0';
                    }
                    else
                    {
                        if (file > 7) throw new FormatException($"Rank {rank + 1} is too long.");
                        board[rank * 8 + file] = Piece.FromSymbol(c);
                        file++;
                    }
                }
                if (file != 8)
                    throw new FormatException($"Rank {rank + 1} does not have 8 squares.");
            }

            var side = parts[1] switch
            {
                "w" => PieceColor.White,
                "b" => PieceColor.Black,
                _ => throw new FormatException("Side to move must be 'w' or 'b'."),
            };

            var castling = CastlingRights.None;
            if (parts[2] != "-")
            {
                foreach (char c in parts[2])
                {
                    castling |= c switch
                    {
                        'K' => CastlingRights.WhiteKingside,
                        'Q' => CastlingRights.WhiteQueenside,
                        'k' => CastlingRights.BlackKingside,
                        'q' => CastlingRights.BlackQueenside,
                        _ => throw new FormatException($"Bad castling flag '{c}'."),
                    };
                }
            }

            Square? enPassant = parts[3] == "-" ? (Square?)null : Square.Parse(parts[3]);
            int halfmove = parts.Length > 4 ? int.Parse(parts[4]) : 0;
            int fullmove = parts.Length > 5 ? int.Parse(parts[5]) : 1;

            return new Position(board, side, castling, enPassant, halfmove, fullmove);
        }

        public string ToFen() => $"{PlacementKey()} {HalfmoveClock} {FullmoveNumber}";

        /// <summary>FEN without the clocks: two positions with the same key count as a repetition.</summary>
        public string PlacementKey()
        {
            var sb = new StringBuilder();
            for (int rank = 7; rank >= 0; rank--)
            {
                int empty = 0;
                for (int file = 0; file < 8; file++)
                {
                    var piece = _board[rank * 8 + file];
                    if (piece.HasValue)
                    {
                        if (empty > 0) sb.Append(empty);
                        empty = 0;
                        sb.Append(piece.Value.Symbol);
                    }
                    else
                    {
                        empty++;
                    }
                }
                if (empty > 0) sb.Append(empty);
                if (rank > 0) sb.Append('/');
            }

            sb.Append(SideToMove == PieceColor.White ? " w " : " b ");

            if (Castling == CastlingRights.None)
            {
                sb.Append('-');
            }
            else
            {
                if ((Castling & CastlingRights.WhiteKingside) != 0) sb.Append('K');
                if ((Castling & CastlingRights.WhiteQueenside) != 0) sb.Append('Q');
                if ((Castling & CastlingRights.BlackKingside) != 0) sb.Append('k');
                if ((Castling & CastlingRights.BlackQueenside) != 0) sb.Append('q');
            }

            sb.Append(' ').Append(EnPassant.HasValue ? EnPassant.Value.ToString() : "-");
            return sb.ToString();
        }

        public override string ToString() => ToFen();
    }
}
