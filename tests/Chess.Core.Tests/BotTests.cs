using System.Diagnostics;
using Chess.Core;
using Xunit;

namespace Chess.Core.Tests
{
    public class BotTests
    {
        [Theory]
        [InlineData(BotLevel.Medium)]
        [InlineData(BotLevel.Hard)]
        public void FindsMateInOne(BotLevel level)
        {
            // Rook to a8 is mate: the black king is boxed in by its own pawns.
            var position = Position.FromFen("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1");
            var move = new Bot(level, seed: 1).ChooseMove(position);
            Assert.Equal("a1a8", move?.ToUci());
        }

        [Fact]
        public void TakesAHangingQueen()
        {
            var position = Position.FromFen("4k3/8/8/3q4/8/8/8/3RK3 w - - 0 1");
            var move = new Bot(BotLevel.Hard, seed: 1).ChooseMove(position);
            Assert.Equal("d1d5", move?.ToUci());
        }

        [Fact]
        public void ReturnsNullWhenNoMoves()
        {
            var position = Position.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");
            Assert.Null(new Bot(BotLevel.Hard).ChooseMove(position));
        }

        [Fact]
        public void HardBotAnswersQuicklyFromTheStart()
        {
            var watch = Stopwatch.StartNew();
            var move = new Bot(BotLevel.Hard, seed: 1).ChooseMove(Position.Start);
            Assert.NotNull(move);
            Assert.True(watch.ElapsedMilliseconds < 5000, $"took {watch.ElapsedMilliseconds} ms");
        }

        [Fact]
        public void BotsCanPlayAWholeGame()
        {
            var game = new Game();
            var white = new Bot(BotLevel.Medium, seed: 1);
            var black = new Bot(BotLevel.Easy, seed: 2);
            for (int ply = 0; ply < 200 && !game.IsOver; ply++)
            {
                var bot = game.Current.SideToMove == PieceColor.White ? white : black;
                game.MakeMove(bot.ChooseMove(game.Current)!.Value);
            }
            Assert.True(game.Moves.Count > 0);
        }
    }
}
