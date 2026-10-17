using System.Collections.Generic;
using Chess.Core;
using Xunit;

namespace Chess.Core.Tests
{
    public class ArmyTests
    {
        private static Army Build(params (string square, PieceType type)[] pieces)
        {
            var dict = new Dictionary<Square, PieceType>();
            foreach (var (square, type) in pieces)
                dict[Square.Parse(square)] = type;
            return new Army(dict);
        }

        [Fact]
        public void ClassicArmyCostsExactlyTheBudget()
        {
            Assert.Equal(Army.Budget, Army.Classic.Cost);
            Assert.True(Army.Classic.IsValid);
        }

        [Fact]
        public void TwoClassicArmiesGiveTheStandardStartPosition()
        {
            var position = Army.ToPosition(Army.Classic, Army.Classic);
            Assert.Equal(Position.StartFen, position.ToFen());
        }

        [Fact]
        public void FourQueensAndThreePawnsFitTheBudget()
        {
            var army = Build(
                ("a1", PieceType.Queen), ("b1", PieceType.Queen), ("g1", PieceType.Queen), ("h1", PieceType.Queen),
                ("e1", PieceType.King),
                ("d2", PieceType.Pawn), ("e2", PieceType.Pawn), ("f2", PieceType.Pawn));
            Assert.Equal(39, army.Cost);
            Assert.True(army.IsValid);
        }

        [Fact]
        public void FiveQueensAreOverBudget()
        {
            var army = Build(
                ("a1", PieceType.Queen), ("b1", PieceType.Queen), ("c1", PieceType.Queen),
                ("g1", PieceType.Queen), ("h1", PieceType.Queen), ("e1", PieceType.King));
            Assert.Contains(ArmyError.OverBudget, army.Validate());
        }

        [Fact]
        public void ArmyNeedsExactlyOneKing()
        {
            Assert.Contains(ArmyError.KingCount, Build(("d1", PieceType.Queen)).Validate());
            Assert.Contains(ArmyError.KingCount,
                Build(("d1", PieceType.King), ("e1", PieceType.King)).Validate());
        }

        [Fact]
        public void PiecesMustStayOnHomeRanks()
        {
            var errors = Build(("e1", PieceType.King), ("e3", PieceType.Knight), ("a1", PieceType.Pawn)).Validate();
            Assert.Contains(ArmyError.OutsideHomeRanks, errors);
            Assert.Contains(ArmyError.PawnOnFirstRank, errors);
        }

        [Fact]
        public void CastlingOnlyWhereKingAndRookStandOnClassicSquares()
        {
            var white = Build(("e1", PieceType.King), ("h1", PieceType.Rook), ("a1", PieceType.Queen));
            var position = Army.ToPosition(white, Army.Classic);
            Assert.Equal(
                CastlingRights.WhiteKingside | CastlingRights.BlackKingside | CastlingRights.BlackQueenside,
                position.Castling);
        }

        [Fact]
        public void BlackArmyIsMirrored()
        {
            var black = Build(("e1", PieceType.King), ("a1", PieceType.Queen), ("c2", PieceType.Pawn));
            var position = Army.ToPosition(Army.Classic, black);
            Assert.Equal(new Piece(PieceType.King, PieceColor.Black), position[Square.Parse("e8")]);
            Assert.Equal(new Piece(PieceType.Queen, PieceColor.Black), position[Square.Parse("a8")]);
            Assert.Equal(new Piece(PieceType.Pawn, PieceColor.Black), position[Square.Parse("c7")]);
        }

        [Fact]
        public void CustomArmyGameIsPlayable()
        {
            var white = Build(
                ("a1", PieceType.Queen), ("b1", PieceType.Queen), ("g1", PieceType.Queen), ("h1", PieceType.Queen),
                ("e1", PieceType.King), ("d2", PieceType.Pawn), ("e2", PieceType.Pawn), ("f2", PieceType.Pawn));
            var game = new Game(Army.ToPosition(white, Army.Classic));
            Assert.Equal(GameStatus.Ongoing, game.Status);
            Assert.NotEmpty(game.LegalMoves);
            game.MakeMove("e2e4");
            Assert.Equal(PieceColor.Black, game.Current.SideToMove);
        }

        [Fact]
        public void CodeRoundTrips()
        {
            string code = Army.Classic.ToCode();
            Assert.Equal(code, Army.FromCode(code)!.ToCode());
            Assert.Null(Army.FromCode("Ke1,Xz9"));
        }

        [Fact]
        public void AllPresetsAreValid()
        {
            foreach (var (name, army) in Army.Presets)
                Assert.True(army.IsValid, name + ": " + string.Join(", ", army.Validate()));
        }
    }
}
