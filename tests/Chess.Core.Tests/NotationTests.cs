using Chess.Core;
using Xunit;

namespace Chess.Core.Tests
{
    public class NotationTests
    {
        private static string San(string fen, string uci, NotationLanguage language = NotationLanguage.English)
        {
            var game = new Game(Position.FromFen(fen));
            var move = game.FindMove(uci) ?? game.FindMove(uci + "q");
            Assert.NotNull(move);
            return Notation.ToSan(game.Current, move!.Value, language);
        }

        [Theory]
        [InlineData(Position.StartFen, "e2e4", "e4")]
        [InlineData(Position.StartFen, "g1f3", "Nf3")]
        [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1g1", "O-O")]
        [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1c1", "O-O-O")]
        [InlineData("4k3/8/8/3p4/4P3/8/8/4K3 w - - 0 1", "e4d5", "exd5")]
        [InlineData("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1", "e5d6", "exd6")]
        [InlineData("7k/P7/8/8/8/8/8/4K3 w - - 0 1", "a7a8", "a8=Q+")]
        [InlineData("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1", "a1a8", "Ra8#")]
        public void WritesShortAlgebraicNotation(string fen, string uci, string expected) =>
            Assert.Equal(expected, San(fen, uci));

        [Theory]
        // Two knights can reach d2: name the file.
        [InlineData("4k3/8/8/8/8/8/8/1N2KN2 w - - 0 1", "b1d2", "Nbd2")]
        // Two rooks on the same file can reach e4: name the rank.
        [InlineData("7k/4R3/8/8/8/8/4R3/K7 w - - 0 1", "e2e4", "R2e4")]
        // Queens on e4 and h1 share the rank and the file of h4: both are needed.
        [InlineData("2k5/8/8/8/4Q2Q/8/8/K6Q w - - 0 1", "h4e1", "Qh4e1")]
        public void DisambiguatesWhenTwoPiecesCanMove(string fen, string uci, string expected) =>
            Assert.Equal(expected, San(fen, uci));

        [Fact]
        public void WritesRussianLetters()
        {
            Assert.Equal("Кf3", San(Position.StartFen, "g1f3", NotationLanguage.Russian));
            Assert.Equal("Лa8#", San("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1", "a1a8", NotationLanguage.Russian));
        }

        [Fact]
        public void GameRecordsMovesAndUndoRemovesThem()
        {
            var game = new Game();
            game.MakeMove("e2e4");
            game.MakeMove("e7e5");
            game.MakeMove("g1f3");
            Assert.Equal(new[] { "e4", "e5", "Nf3" }, game.SanMoves);
            Assert.Equal(new[] { "e4", "e5", "Кf3" }, game.MovesIn(NotationLanguage.Russian));
            game.Undo();
            Assert.Equal(new[] { "e4", "e5" }, game.SanMoves);
        }

        [Fact]
        public void PgnListsMovesAndResult()
        {
            var game = new Game();
            foreach (var uci in new[] { "f2f3", "e7e5", "g2g4", "d8h4" })
                game.MakeMove(uci);
            string pgn = game.ToPgn("Denis", "Bot");
            Assert.Contains("[White \"Denis\"]", pgn);
            Assert.Contains("[Result \"0-1\"]", pgn);
            Assert.EndsWith("1. f3 e5 2. g4 Qh4# 0-1", pgn);
        }
    }
}
