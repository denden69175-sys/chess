using Chess.Core;
using Xunit;

namespace Chess.Core.Tests
{
    /// <summary>
    /// Perft counts every legal move sequence to a fixed depth. The expected numbers are the
    /// well-known reference values from the Chess Programming Wiki, so any bug in move
    /// generation (castling, en passant, promotion, pins) shows up as a wrong count.
    /// </summary>
    public class PerftTests
    {
        private static long Perft(Position position, int depth)
        {
            var moves = MoveGenerator.GenerateLegal(position);
            if (depth == 1)
                return moves.Count;
            long nodes = 0;
            foreach (var move in moves)
                nodes += Perft(position.Apply(move), depth - 1);
            return nodes;
        }

        [Theory]
        [InlineData(1, 20)]
        [InlineData(2, 400)]
        [InlineData(3, 8902)]
        [InlineData(4, 197281)]
        public void StartPosition(int depth, long expected) =>
            Assert.Equal(expected, Perft(Position.Start, depth));

        [Theory]
        [InlineData(1, 48)]
        [InlineData(2, 2039)]
        [InlineData(3, 97862)]
        public void Kiwipete(int depth, long expected) =>
            Assert.Equal(expected, Perft(Position.FromFen(
                "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1"), depth));

        [Theory]
        [InlineData(1, 14)]
        [InlineData(2, 191)]
        [InlineData(3, 2812)]
        [InlineData(4, 43238)]
        public void Position3(int depth, long expected) =>
            Assert.Equal(expected, Perft(Position.FromFen("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1"), depth));

        [Theory]
        [InlineData(1, 6)]
        [InlineData(2, 264)]
        [InlineData(3, 9467)]
        public void Position4(int depth, long expected) =>
            Assert.Equal(expected, Perft(Position.FromFen(
                "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1"), depth));

        [Theory]
        [InlineData(1, 44)]
        [InlineData(2, 1486)]
        [InlineData(3, 62379)]
        public void Position5(int depth, long expected) =>
            Assert.Equal(expected, Perft(Position.FromFen(
                "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8"), depth));
    }
}
