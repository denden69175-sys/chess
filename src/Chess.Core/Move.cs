using System;

namespace Chess.Core
{
    public enum MoveKind
    {
        Normal,
        DoublePawnPush,
        EnPassant,
        CastleKingside,
        CastleQueenside,
        Promotion,
    }

    public readonly struct Move : IEquatable<Move>
    {
        public Move(Square from, Square to, MoveKind kind = MoveKind.Normal, PieceType? promotion = null)
        {
            From = from;
            To = to;
            Kind = kind;
            Promotion = promotion;
        }

        public Square From { get; }
        public Square To { get; }
        public MoveKind Kind { get; }
        public PieceType? Promotion { get; }

        /// <summary>Long algebraic form used by UCI engines, e.g. "e2e4" or "e7e8q".</summary>
        public string ToUci()
        {
            string s = From.ToString() + To;
            if (Promotion.HasValue)
                s += new Piece(Promotion.Value, PieceColor.Black).Symbol;
            return s;
        }

        public bool Equals(Move other) =>
            From == other.From && To == other.To && Kind == other.Kind && Promotion == other.Promotion;

        public override bool Equals(object? obj) => obj is Move other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(From.Index, To.Index, (int)Kind, Promotion);
        public static bool operator ==(Move a, Move b) => a.Equals(b);
        public static bool operator !=(Move a, Move b) => !a.Equals(b);
        public override string ToString() => ToUci();
    }
}
