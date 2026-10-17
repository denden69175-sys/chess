using System;

namespace Chess.Core
{
    /// <summary>A board square. Index 0 is a1, 7 is h1, 63 is h8.</summary>
    public readonly struct Square : IEquatable<Square>
    {
        public Square(int index)
        {
            if (index < 0 || index > 63)
                throw new ArgumentOutOfRangeException(nameof(index));
            Index = index;
        }

        public Square(int file, int rank) : this(rank * 8 + file)
        {
            if (file < 0 || file > 7) throw new ArgumentOutOfRangeException(nameof(file));
            if (rank < 0 || rank > 7) throw new ArgumentOutOfRangeException(nameof(rank));
        }

        public int Index { get; }

        /// <summary>0 = file a, 7 = file h.</summary>
        public int File => Index % 8;

        /// <summary>0 = rank 1, 7 = rank 8.</summary>
        public int Rank => Index / 8;

        public static bool IsOnBoard(int file, int rank) => file >= 0 && file < 8 && rank >= 0 && rank < 8;

        public static Square Parse(string name)
        {
            if (name == null || name.Length != 2)
                throw new FormatException($"Bad square '{name}'.");
            int file = name[0] - 'a';
            int rank = name[1] - '1';
            if (!IsOnBoard(file, rank))
                throw new FormatException($"Bad square '{name}'.");
            return new Square(file, rank);
        }

        public bool Equals(Square other) => Index == other.Index;
        public override bool Equals(object? obj) => obj is Square other && Equals(other);
        public override int GetHashCode() => Index;
        public static bool operator ==(Square a, Square b) => a.Index == b.Index;
        public static bool operator !=(Square a, Square b) => a.Index != b.Index;
        public override string ToString() => $"{(char)('a' + File)}{(char)('1' + Rank)}";
    }
}
