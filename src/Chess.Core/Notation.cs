using System.Collections.Generic;
using System.Text;

namespace Chess.Core
{
    public enum NotationLanguage
    {
        /// <summary>International letters K Q R B N, as used in PGN files.</summary>
        English,

        /// <summary>Russian letters: Кр Ф Л С К.</summary>
        Russian,
    }

    /// <summary>Writes moves in short algebraic notation (SAN), e.g. "e4", "Nf3", "exd5", "O-O", "e8=Q#".</summary>
    public static class Notation
    {
        /// <summary>
        /// SAN for a legal move in the given position. Check and mate marks are added by
        /// looking at the position after the move.
        /// </summary>
        public static string ToSan(Position position, Move move, NotationLanguage language = NotationLanguage.English)
        {
            var sb = new StringBuilder();
            var piece = position[move.From] ?? throw new System.ArgumentException($"No piece on {move.From}.");

            if (move.Kind == MoveKind.CastleKingside)
            {
                sb.Append("O-O");
            }
            else if (move.Kind == MoveKind.CastleQueenside)
            {
                sb.Append("O-O-O");
            }
            else
            {
                bool capture = position[move.To].HasValue || move.Kind == MoveKind.EnPassant;
                if (piece.Type == PieceType.Pawn)
                {
                    if (capture)
                        sb.Append((char)('a' + move.From.File));
                }
                else
                {
                    sb.Append(Letter(piece.Type, language));
                    sb.Append(Disambiguation(position, move, piece));
                }

                if (capture)
                    sb.Append('x');
                sb.Append(move.To);

                if (move.Promotion.HasValue)
                    sb.Append('=').Append(Letter(move.Promotion.Value, language));
            }

            var after = position.Apply(move);
            if (after.IsInCheck(after.SideToMove))
                sb.Append(MoveGenerator.GenerateLegal(after).Count == 0 ? '#' : '+');

            return sb.ToString();
        }

        public static string Letter(PieceType type, NotationLanguage language)
        {
            if (language == NotationLanguage.Russian)
            {
                return type switch
                {
                    PieceType.King => "Кр",
                    PieceType.Queen => "Ф",
                    PieceType.Rook => "Л",
                    PieceType.Bishop => "С",
                    PieceType.Knight => "К",
                    _ => "",
                };
            }
            return type switch
            {
                PieceType.King => "K",
                PieceType.Queen => "Q",
                PieceType.Rook => "R",
                PieceType.Bishop => "B",
                PieceType.Knight => "N",
                _ => "",
            };
        }

        /// <summary>
        /// When two pieces of the same kind can reach the square, add the file, else the rank,
        /// else both, of the moving piece ("Nbd2", "R1e2", "Qh4e1").
        /// </summary>
        private static string Disambiguation(Position position, Move move, Piece piece)
        {
            if (piece.Type == PieceType.King)
                return "";

            var rivals = new List<Square>();
            foreach (var other in MoveGenerator.GenerateLegal(position))
            {
                if (other.To == move.To && other.From != move.From && position[other.From] == piece
                    && !rivals.Contains(other.From))
                    rivals.Add(other.From);
            }
            if (rivals.Count == 0)
                return "";

            bool sameFile = rivals.Exists(s => s.File == move.From.File);
            bool sameRank = rivals.Exists(s => s.Rank == move.From.Rank);
            string file = ((char)('a' + move.From.File)).ToString();
            string rank = ((char)('1' + move.From.Rank)).ToString();
            if (!sameFile)
                return file;
            if (!sameRank)
                return rank;
            return file + rank;
        }
    }
}
