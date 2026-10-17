using System.Collections.Generic;
using System.Linq;

namespace Chess.Core
{
    public enum ArmyError
    {
        /// <summary>The army must have exactly one king.</summary>
        KingCount,

        /// <summary>The pieces cost more than <see cref="Army.Budget"/>.</summary>
        OverBudget,

        /// <summary>More than <see cref="Army.MaxPieces"/> pieces, king included.</summary>
        TooManyPieces,

        /// <summary>A piece stands outside the two home ranks.</summary>
        OutsideHomeRanks,

        /// <summary>A pawn stands on the first rank.</summary>
        PawnOnFirstRank,
    }

    /// <summary>
    /// A player-built set of pieces for the custom-army mode, described from White's side:
    /// rank 0 is the back rank, rank 1 the pawn rank. Black's army is mirrored onto ranks 8 and 7.
    /// </summary>
    public sealed class Army
    {
        /// <summary>What the classic set costs: 8 pawns + 2 knights + 2 bishops + 2 rooks + queen.</summary>
        public const int Budget = 39;

        public const int MaxPieces = 16;

        private readonly Dictionary<Square, PieceType> _pieces;

        public Army(IDictionary<Square, PieceType> pieces)
        {
            _pieces = new Dictionary<Square, PieceType>(pieces);
        }

        public IReadOnlyDictionary<Square, PieceType> Pieces => _pieces;

        public int Cost => _pieces.Values.Sum(PieceValues.Of);

        public static Army Classic => new Army(new Dictionary<Square, PieceType>
        {
            [Square.Parse("a1")] = PieceType.Rook,
            [Square.Parse("b1")] = PieceType.Knight,
            [Square.Parse("c1")] = PieceType.Bishop,
            [Square.Parse("d1")] = PieceType.Queen,
            [Square.Parse("e1")] = PieceType.King,
            [Square.Parse("f1")] = PieceType.Bishop,
            [Square.Parse("g1")] = PieceType.Knight,
            [Square.Parse("h1")] = PieceType.Rook,
            [Square.Parse("a2")] = PieceType.Pawn,
            [Square.Parse("b2")] = PieceType.Pawn,
            [Square.Parse("c2")] = PieceType.Pawn,
            [Square.Parse("d2")] = PieceType.Pawn,
            [Square.Parse("e2")] = PieceType.Pawn,
            [Square.Parse("f2")] = PieceType.Pawn,
            [Square.Parse("g2")] = PieceType.Pawn,
            [Square.Parse("h2")] = PieceType.Pawn,
        });

        /// <summary>
        /// Short text form, e.g. "Ke1,Qd1,Pe2", used to save armies and send them to the server.
        /// Squares are always given from White's side.
        /// </summary>
        public string ToCode()
        {
            var parts = new List<string>();
            foreach (var pair in _pieces.OrderBy(p => p.Key.Index))
                parts.Add(new Piece(pair.Value, PieceColor.White).Symbol + pair.Key.ToString());
            return string.Join(",", parts);
        }

        /// <summary>Reads <see cref="ToCode"/> output; returns null for malformed text.</summary>
        public static Army? FromCode(string? code)
        {
            if (code == null)
                return null;
            var pieces = new Dictionary<Square, PieceType>();
            foreach (var part in code.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                string item = part.Trim();
                if (item.Length != 3)
                    return null;
                try
                {
                    var type = Piece.FromSymbol(char.ToUpperInvariant(item[0])).Type;
                    var square = Square.Parse(item.Substring(1));
                    if (pieces.ContainsKey(square))
                        return null;
                    pieces[square] = type;
                }
                catch (System.FormatException)
                {
                    return null;
                }
            }
            return new Army(pieces);
        }

        /// <summary>Ready-made armies to start from in the army editor. All fit the budget.</summary>
        public static IReadOnlyList<(string Name, Army Army)> Presets => new List<(string, Army)>
        {
            ("Классика", Classic),
            ("Четыре ферзя", FromCode("Qa1,Qb1,Ke1,Qg1,Qh1,Pd2,Pe2,Pf2")!),
            ("Конница", FromCode("Na1,Nb1,Nc1,Nd1,Ke1,Nf1,Ng1,Nh1,Pa2,Pb2,Nc2,Nd2,Ne2,Nf2,Pg2,Ph2")!),
            ("Ладейный вал", FromCode("Ra1,Rb1,Rc1,Rd1,Ke1,Rf1,Rg1,Rh1,Pc2,Pd2,Pe2,Pf2")!),
        };

        public List<ArmyError> Validate()
        {
            var errors = new List<ArmyError>();
            if (_pieces.Values.Count(t => t == PieceType.King) != 1)
                errors.Add(ArmyError.KingCount);
            if (Cost > Budget)
                errors.Add(ArmyError.OverBudget);
            if (_pieces.Count > MaxPieces)
                errors.Add(ArmyError.TooManyPieces);
            if (_pieces.Keys.Any(s => s.Rank > 1))
                errors.Add(ArmyError.OutsideHomeRanks);
            if (_pieces.Any(p => p.Value == PieceType.Pawn && p.Key.Rank == 0))
                errors.Add(ArmyError.PawnOnFirstRank);
            return errors;
        }

        public bool IsValid => Validate().Count == 0;

        /// <summary>
        /// Builds the starting position for two armies. Castling is allowed only on a side where
        /// the king and that rook stand on their classic squares.
        /// </summary>
        public static Position ToPosition(Army white, Army black)
        {
            var board = new Piece?[64];
            foreach (var pair in white._pieces)
                board[pair.Key.Index] = new Piece(pair.Value, PieceColor.White);
            foreach (var pair in black._pieces)
                board[new Square(pair.Key.File, 7 - pair.Key.Rank).Index] = new Piece(pair.Value, PieceColor.Black);

            var castling = CastlingRights.None;
            castling |= RightIf(board, 4, 7, PieceColor.White, CastlingRights.WhiteKingside);
            castling |= RightIf(board, 4, 0, PieceColor.White, CastlingRights.WhiteQueenside);
            castling |= RightIf(board, 60, 63, PieceColor.Black, CastlingRights.BlackKingside);
            castling |= RightIf(board, 60, 56, PieceColor.Black, CastlingRights.BlackQueenside);

            return new Position(board, PieceColor.White, castling, null, 0, 1);
        }

        private static CastlingRights RightIf(Piece?[] board, int king, int rook, PieceColor color, CastlingRights right)
        {
            bool ok = board[king] == new Piece(PieceType.King, color) && board[rook] == new Piece(PieceType.Rook, color);
            return ok ? right : CastlingRights.None;
        }
    }

    public static class PieceValues
    {
        /// <summary>Classic material values; the king is free because every army has exactly one.</summary>
        public static int Of(PieceType type) => type switch
        {
            PieceType.Pawn => 1,
            PieceType.Knight => 3,
            PieceType.Bishop => 3,
            PieceType.Rook => 5,
            PieceType.Queen => 9,
            _ => 0,
        };
    }
}
