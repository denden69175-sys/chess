using Chess.Core;
using Xunit;

namespace Chess.Core.Tests
{
    public class GameTests
    {
        [Fact]
        public void FoolsMateIsCheckmateForBlack()
        {
            var game = new Game();
            foreach (var uci in new[] { "f2f3", "e7e5", "g2g4", "d8h4" })
                game.MakeMove(uci);

            Assert.Equal(GameStatus.Checkmate, game.Status);
            Assert.Equal(PieceColor.Black, game.Winner);
            Assert.Empty(game.LegalMoves);
        }

        [Fact]
        public void StalemateIsDetected()
        {
            var game = new Game(Position.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1"));
            Assert.Equal(GameStatus.Stalemate, game.Status);
            Assert.Null(game.Winner);
        }

        [Fact]
        public void IllegalMoveIsRejected()
        {
            var game = new Game();
            Assert.Null(game.FindMove("e2e5"));
            Assert.Throws<System.InvalidOperationException>(() => game.MakeMove("e2e5"));
        }

        [Fact]
        public void PinnedPieceCannotMove()
        {
            // The knight on e2 is pinned to the king by the rook on e8.
            var game = new Game(Position.FromFen("4r1k1/8/8/8/8/8/4N3/4K3 w - - 0 1"));
            Assert.DoesNotContain(game.LegalMoves, m => m.From == Square.Parse("e2"));
        }

        [Fact]
        public void ThreefoldRepetitionIsADraw()
        {
            var game = new Game();
            foreach (var uci in new[] { "g1f3", "g8f6", "f3g1", "f6g8", "g1f3", "g8f6", "f3g1", "f6g8" })
                game.MakeMove(uci);
            Assert.Equal(GameStatus.ThreefoldRepetition, game.Status);
        }

        [Fact]
        public void FiftyMoveRuleIsADraw()
        {
            var game = new Game(Position.FromFen("4k3/8/8/8/8/8/R7/4K3 w - - 99 80"));
            game.MakeMove("a2a3");
            Assert.Equal(GameStatus.FiftyMoveRule, game.Status);
        }

        [Theory]
        [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 1", true)]
        [InlineData("4k3/8/8/8/8/8/8/2B1K3 w - - 0 1", true)]
        [InlineData("4k3/8/8/8/8/8/8/1N2K3 w - - 0 1", true)]
        [InlineData("4k3/8/8/8/8/8/8/R3K3 w - - 0 1", false)]
        [InlineData("4k3/8/8/8/8/8/P7/4K3 w - - 0 1", false)]
        public void InsufficientMaterial(string fen, bool expected) =>
            Assert.Equal(expected, Game.HasInsufficientMaterial(Position.FromFen(fen)));

        [Fact]
        public void CastlingMovesTheRookToo()
        {
            var game = new Game(Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1"));
            game.MakeMove("e1g1");
            Assert.Equal(new Piece(PieceType.Rook, PieceColor.White), game.Current[Square.Parse("f1")]);
            Assert.Null(game.Current[Square.Parse("h1")]);
            Assert.Equal(CastlingRights.BlackKingside | CastlingRights.BlackQueenside, game.Current.Castling);
        }

        [Fact]
        public void CannotCastleThroughCheck()
        {
            // The black rook on f8 covers f1.
            var game = new Game(Position.FromFen("4kr2/8/8/8/8/8/8/4K2R w K - 0 1"));
            Assert.Null(game.FindMove("e1g1"));
        }

        [Fact]
        public void EnPassantRemovesThePassedPawn()
        {
            var game = new Game(Position.FromFen("4k3/3p4/8/4P3/8/8/8/4K3 b - - 0 1"));
            game.MakeMove("d7d5");
            game.MakeMove("e5d6");
            Assert.Null(game.Current[Square.Parse("d5")]);
            Assert.Equal(new Piece(PieceType.Pawn, PieceColor.White), game.Current[Square.Parse("d6")]);
        }

        [Fact]
        public void PawnPromotesToTheChosenPiece()
        {
            var game = new Game(Position.FromFen("8/P6k/8/8/8/8/8/4K3 w - - 0 1"));
            game.MakeMove("a7a8n");
            Assert.Equal(new Piece(PieceType.Knight, PieceColor.White), game.Current[Square.Parse("a8")]);
        }

        [Fact]
        public void UndoRestoresThePreviousPosition()
        {
            var game = new Game();
            game.MakeMove("e2e4");
            game.Undo();
            Assert.Equal(Position.StartFen, game.Current.ToFen());
            Assert.Equal(20, game.LegalMoves.Count);
        }

        [Fact]
        public void FenRoundTrips()
        {
            const string fen = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";
            Assert.Equal(fen, Position.FromFen(fen).ToFen());
        }
    }
}
