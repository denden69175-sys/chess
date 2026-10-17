using System.Collections.Generic;

namespace Chess.Core
{
    public static class MoveGenerator
    {
        private static readonly (int df, int dr)[] KnightSteps =
        {
            (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2),
        };

        private static readonly (int df, int dr)[] KingSteps =
        {
            (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1),
        };

        private static readonly (int df, int dr)[] RookDirections = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        private static readonly (int df, int dr)[] BishopDirections = { (1, 1), (1, -1), (-1, 1), (-1, -1) };

        private static readonly PieceType[] PromotionTypes =
        {
            PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight,
        };

        /// <summary>All moves for the side to move that do not leave its own king in check.</summary>
        public static List<Move> GenerateLegal(Position position)
        {
            var legal = new List<Move>();
            var us = position.SideToMove;
            foreach (var move in GeneratePseudoLegal(position))
            {
                if (!position.Apply(move).IsInCheck(us))
                    legal.Add(move);
            }
            return legal;
        }

        /// <summary>Moves that follow piece movement rules but may leave the own king in check.</summary>
        public static List<Move> GeneratePseudoLegal(Position position)
        {
            var moves = new List<Move>();
            var us = position.SideToMove;
            for (int i = 0; i < 64; i++)
            {
                var piece = position[i];
                if (!piece.HasValue || piece.Value.Color != us)
                    continue;

                var from = new Square(i);
                switch (piece.Value.Type)
                {
                    case PieceType.Pawn:
                        AddPawnMoves(position, from, us, moves);
                        break;
                    case PieceType.Knight:
                        AddSteps(position, from, us, KnightSteps, moves);
                        break;
                    case PieceType.Bishop:
                        AddSlides(position, from, us, BishopDirections, moves);
                        break;
                    case PieceType.Rook:
                        AddSlides(position, from, us, RookDirections, moves);
                        break;
                    case PieceType.Queen:
                        AddSlides(position, from, us, RookDirections, moves);
                        AddSlides(position, from, us, BishopDirections, moves);
                        break;
                    case PieceType.King:
                        AddSteps(position, from, us, KingSteps, moves);
                        AddCastling(position, from, us, moves);
                        break;
                }
            }
            return moves;
        }

        public static bool IsSquareAttacked(Position position, Square square, PieceColor by)
        {
            int file = square.File, rank = square.Rank;

            // A pawn attacks diagonally forward, so look one rank "behind" the square from the attacker's side.
            int pawnRank = by == PieceColor.White ? rank - 1 : rank + 1;
            foreach (int df in new[] { -1, 1 })
            {
                if (Is(position, file + df, pawnRank, PieceType.Pawn, by))
                    return true;
            }

            foreach (var (df, dr) in KnightSteps)
                if (Is(position, file + df, rank + dr, PieceType.Knight, by))
                    return true;

            foreach (var (df, dr) in KingSteps)
                if (Is(position, file + df, rank + dr, PieceType.King, by))
                    return true;

            return SlidingAttack(position, file, rank, by, RookDirections, PieceType.Rook)
                || SlidingAttack(position, file, rank, by, BishopDirections, PieceType.Bishop);
        }

        private static bool Is(Position position, int file, int rank, PieceType type, PieceColor color)
        {
            if (!Square.IsOnBoard(file, rank))
                return false;
            var piece = position[rank * 8 + file];
            return piece.HasValue && piece.Value.Type == type && piece.Value.Color == color;
        }

        private static bool SlidingAttack(
            Position position, int file, int rank, PieceColor by, (int df, int dr)[] directions, PieceType slider)
        {
            foreach (var (df, dr) in directions)
            {
                int f = file + df, r = rank + dr;
                while (Square.IsOnBoard(f, r))
                {
                    var piece = position[r * 8 + f];
                    if (piece.HasValue)
                    {
                        if (piece.Value.Color == by && (piece.Value.Type == slider || piece.Value.Type == PieceType.Queen))
                            return true;
                        break;
                    }
                    f += df;
                    r += dr;
                }
            }
            return false;
        }

        private static void AddPawnMoves(Position position, Square from, PieceColor us, List<Move> moves)
        {
            int dir = us == PieceColor.White ? 1 : -1;
            int startRank = us == PieceColor.White ? 1 : 6;
            int lastRank = us == PieceColor.White ? 7 : 0;
            int file = from.File, rank = from.Rank;

            int oneRank = rank + dir;
            if (!Square.IsOnBoard(file, oneRank))
                return;

            if (!position[oneRank * 8 + file].HasValue)
            {
                AddPawnMove(from, new Square(file, oneRank), oneRank == lastRank, moves);
                int twoRank = rank + 2 * dir;
                if (rank == startRank && !position[twoRank * 8 + file].HasValue)
                    moves.Add(new Move(from, new Square(file, twoRank), MoveKind.DoublePawnPush));
            }

            foreach (int df in new[] { -1, 1 })
            {
                int f = file + df;
                if (!Square.IsOnBoard(f, oneRank))
                    continue;
                var to = new Square(f, oneRank);
                var target = position[to];
                if (target.HasValue && target.Value.Color != us)
                    AddPawnMove(from, to, oneRank == lastRank, moves);
                else if (!target.HasValue && position.EnPassant == to)
                    moves.Add(new Move(from, to, MoveKind.EnPassant));
            }
        }

        private static void AddPawnMove(Square from, Square to, bool promotes, List<Move> moves)
        {
            if (!promotes)
            {
                moves.Add(new Move(from, to));
                return;
            }
            foreach (var type in PromotionTypes)
                moves.Add(new Move(from, to, MoveKind.Promotion, type));
        }

        private static void AddSteps(
            Position position, Square from, PieceColor us, (int df, int dr)[] steps, List<Move> moves)
        {
            foreach (var (df, dr) in steps)
            {
                int f = from.File + df, r = from.Rank + dr;
                if (!Square.IsOnBoard(f, r))
                    continue;
                var target = position[r * 8 + f];
                if (!target.HasValue || target.Value.Color != us)
                    moves.Add(new Move(from, new Square(f, r)));
            }
        }

        private static void AddSlides(
            Position position, Square from, PieceColor us, (int df, int dr)[] directions, List<Move> moves)
        {
            foreach (var (df, dr) in directions)
            {
                int f = from.File + df, r = from.Rank + dr;
                while (Square.IsOnBoard(f, r))
                {
                    var target = position[r * 8 + f];
                    if (target.HasValue)
                    {
                        if (target.Value.Color != us)
                            moves.Add(new Move(from, new Square(f, r)));
                        break;
                    }
                    moves.Add(new Move(from, new Square(f, r)));
                    f += df;
                    r += dr;
                }
            }
        }

        private static void AddCastling(Position position, Square from, PieceColor us, List<Move> moves)
        {
            int rank = us == PieceColor.White ? 0 : 7;
            if (from != new Square(4, rank))
                return;

            var kingside = us == PieceColor.White ? CastlingRights.WhiteKingside : CastlingRights.BlackKingside;
            var queenside = us == PieceColor.White ? CastlingRights.WhiteQueenside : CastlingRights.BlackQueenside;
            var them = us.Opponent();
            var rook = new Piece(PieceType.Rook, us);

            bool canKingside = (position.Castling & kingside) != 0
                && position[rank * 8 + 7] == rook
                && IsEmpty(position, rank, 5, 6);
            bool canQueenside = (position.Castling & queenside) != 0
                && position[rank * 8 + 0] == rook
                && IsEmpty(position, rank, 1, 3);

            if (!canKingside && !canQueenside)
                return;

            // The king may not castle out of, through, or into check.
            if (IsSquareAttacked(position, from, them))
                return;

            if (canKingside
                && !IsSquareAttacked(position, new Square(5, rank), them)
                && !IsSquareAttacked(position, new Square(6, rank), them))
                moves.Add(new Move(from, new Square(6, rank), MoveKind.CastleKingside));

            if (canQueenside
                && !IsSquareAttacked(position, new Square(3, rank), them)
                && !IsSquareAttacked(position, new Square(2, rank), them))
                moves.Add(new Move(from, new Square(2, rank), MoveKind.CastleQueenside));
        }

        private static bool IsEmpty(Position position, int rank, int fromFile, int toFile)
        {
            for (int f = fromFile; f <= toFile; f++)
                if (position[rank * 8 + f].HasValue)
                    return false;
            return true;
        }
    }
}
