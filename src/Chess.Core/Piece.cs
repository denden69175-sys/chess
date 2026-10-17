using System;

namespace Chess.Core
{
    public enum PieceColor
    {
        White,
        Black,
    }

    public enum PieceType
    {
        Pawn,
        Knight,
        Bishop,
        Rook,
        Queen,
        King,
    }

    public static class PieceColorExtensions
    {
        public static PieceColor Opponent(this PieceColor color) =>
            color == PieceColor.White ? PieceColor.Black : PieceColor.White;
    }

    public readonly struct Piece : IEquatable<Piece>
    {
        public Piece(PieceType type, PieceColor color)
        {
            Type = type;
            Color = color;
        }

        public PieceType Type { get; }
        public PieceColor Color { get; }

        /// <summary>FEN letter: uppercase for white, lowercase for black.</summary>
        public char Symbol
        {
            get
            {
                char c = Type switch
                {
                    PieceType.Pawn => 'p',
                    PieceType.Knight => 'n',
                    PieceType.Bishop => 'b',
                    PieceType.Rook => 'r',
                    PieceType.Queen => 'q',
                    _ => 'k',
                };
                return Color == PieceColor.White ? char.ToUpperInvariant(c) : c;
            }
        }

        public static Piece FromSymbol(char symbol)
        {
            var color = char.IsUpper(symbol) ? PieceColor.White : PieceColor.Black;
            var type = char.ToLowerInvariant(symbol) switch
            {
                'p' => PieceType.Pawn,
                'n' => PieceType.Knight,
                'b' => PieceType.Bishop,
                'r' => PieceType.Rook,
                'q' => PieceType.Queen,
                'k' => PieceType.King,
                _ => throw new FormatException($"Unknown piece symbol '{symbol}'."),
            };
            return new Piece(type, color);
        }

        public bool Equals(Piece other) => Type == other.Type && Color == other.Color;
        public override bool Equals(object? obj) => obj is Piece other && Equals(other);
        public override int GetHashCode() => ((int)Color << 3) | (int)Type;
        public static bool operator ==(Piece a, Piece b) => a.Equals(b);
        public static bool operator !=(Piece a, Piece b) => !a.Equals(b);
        public override string ToString() => Symbol.ToString();
    }
}
