using System.Collections.Generic;
using System.Linq;

namespace Chess.Core
{
    /// <summary>A chess problem: the side to move mates in <see cref="MateIn"/> moves.</summary>
    public sealed class Puzzle
    {
        public Puzzle(int number, string fen, int mateIn, string solution)
        {
            Number = number;
            Fen = fen;
            MateIn = mateIn;
            Solution = solution;
        }

        /// <summary>1-based number shown to the player; also the puzzle's id in saved progress.</summary>
        public int Number { get; }
        public string Fen { get; }
        public int MateIn { get; }

        /// <summary>The first move of the solution in UCI, used for hints. Any other mating line is accepted too.</summary>
        public string Solution { get; }

        public Position Start => Position.FromFen(Fen);

        /// <summary>Coins for solving it the first time.</summary>
        public int Reward => MateIn == 1 ? 15 : MateIn == 2 ? 30 : 50;
    }

    /// <summary>Mate search for the puzzles: small and exact, fine for mates in one to three.</summary>
    public static class MateSolver
    {
        public static bool IsCheckmate(Position position) =>
            position.IsInCheck(position.SideToMove) && MoveGenerator.GenerateLegal(position).Count == 0;

        /// <summary>
        /// True when, after the attacker's <paramref name="move"/>, the attacker mates in at most
        /// <paramref name="moves"/> moves (counting this one) whatever the defender does.
        /// </summary>
        public static bool Forces(Position position, Move move, int moves)
        {
            var after = position.Apply(move);
            if (IsCheckmate(after))
                return true;
            if (moves <= 1)
                return false;
            var replies = MoveGenerator.GenerateLegal(after);
            if (replies.Count == 0)
                return false; // stalemate
            foreach (var reply in replies)
            {
                var next = after.Apply(reply);
                if (!MoveGenerator.GenerateLegal(next).Any(m => Forces(next, m, moves - 1)))
                    return false;
            }
            return true;
        }

        /// <summary>Every move that mates in at most <paramref name="moves"/> moves.</summary>
        public static List<Move> MatingMoves(Position position, int moves) =>
            MoveGenerator.GenerateLegal(position).Where(m => Forces(position, m, moves)).ToList();

        /// <summary>
        /// The defender's reply for the computer in longer puzzles: the one that leaves the attacker
        /// the fewest ways to finish within <paramref name="attackerMovesLeft"/> moves.
        /// </summary>
        public static Move? BestDefence(Position position, int attackerMovesLeft)
        {
            Move? best = null;
            int fewest = int.MaxValue;
            foreach (var reply in MoveGenerator.GenerateLegal(position))
            {
                int ways = MatingMoves(position.Apply(reply), attackerMovesLeft).Count;
                if (ways < fewest)
                {
                    fewest = ways;
                    best = reply;
                }
            }
            return best;
        }
    }
    /// <summary>
    /// The puzzles in the game: 20 mates in one, then 20 mates in two. They come from positions in
    /// bot games where exactly one first move mates; the tests check every one again.
    /// </summary>
    public static class PuzzleBook
    {
        public static readonly IReadOnlyList<Puzzle> All = new[]
        {
            new Puzzle(1, "r1b5/pp2Q3/4p3/3pn2k/2NP4/P1P5/2P2PPP/R1B1K2R w KQ - 0 21", 1, "e7g5"),
            new Puzzle(2, "r4k1r/ppp2pp1/7p/1PbP4/2K2Pb1/8/P1P5/3q4 b - - 3 25", 1, "d1c2"),
            new Puzzle(3, "5Q2/2pN3k/2P1p1pp/3p1p2/1p3P2/2N5/P1P2PPP/RR4K1 w - - 1 22", 1, "d7f6"),
            new Puzzle(4, "r3r3/ppp4k/2n2p2/7p/4pP2/8/4b1PP/1q2B2K b - - 1 30", 1, "b1e1"),
            new Puzzle(5, "k7/1r6/p1QN2p1/1p1P4/5p2/P6P/P1P2PKP/R2R4 w - - 0 36", 1, "c6b7"),
            new Puzzle(6, "r3k2r/ppp2pp1/6bp/8/1b1pP3/2q3P1/P1P1K2P/3Q1BR1 b kq - 3 20", 1, "c3e3"),
            new Puzzle(7, "3k3r/4b2p/pp4p1/2NBPQP1/3P4/4B2P/PPPN1P2/R4RK1 w - - 2 27", 1, "f5d7"),
            new Puzzle(8, "8/1p3p1p/2p1b2k/2r1n3/1p1pP3/3N4/2P1KPP1/r4B2 b - - 3 29", 1, "c5c2"),
            new Puzzle(9, "Nnb1k3/1p4Q1/6p1/p4p2/3Q4/2P2N2/P3PPPP/R1B1KB1R w KQ - 1 17", 1, "a8c7"),
            new Puzzle(10, "r1b2rk1/ppp2p1p/1qn5/3n4/P1P3P1/K7/R1P5/8 b - - 0 22", 1, "b6b4"),
            new Puzzle(11, "r1b1r3/pp3p1p/1kp5/3RBNp1/P1B5/4P3/1PP2PPP/2K4R w - - 3 16", 1, "a4a5"),
            new Puzzle(12, "2k2b1r/ppp1p1pp/4b3/4Np1P/P2r1n2/R7/1P1p1PP1/1K3B1R b - - 1 19", 1, "d2d1q"),
            new Puzzle(13, "8/3P4/Q5pk/4N3/3N1pp1/8/PP3P2/R4RK1 w - - 1 41", 1, "a6g6"),
            new Puzzle(14, "r1bqk2r/pp4pp/2npp3/3p4/3Pn3/P3pN2/1PP1PPPP/R2QKB1R b KQkq - 1 11", 1, "e3f2"),
            new Puzzle(15, "1r4kr/1pp3pp/p3b3/3NP3/4P3/5Q2/R1P2PPP/4K2R w K - 1 19", 1, "d5e7"),
            new Puzzle(16, "r7/p3kpbr/q3p1p1/7p/3P1n2/8/8/4K2b b - - 3 31", 1, "a6e2"),
            new Puzzle(17, "6k1/2p5/3b3B/5Q2/4BpP1/5N2/PPP2P1P/R4RK1 w - - 5 31", 1, "f5h7"),
            new Puzzle(18, "1k4r1/p4p1p/7p/1P1pn3/p2R1br1/2n5/5P2/5R1K b - - 6 32", 1, "g4h4"),
            new Puzzle(19, "2Q2nk1/1R3Npr/r6p/2B1p3/1P2P2P/3P1N2/5PP1/5RK1 w - - 3 32", 1, "c8f8"),
            new Puzzle(20, "r2k2r1/pbp2pqp/2np4/4b3/3P4/8/PPPP1PBP/R1B2RK1 b - - 0 17", 1, "g7g2"),
            new Puzzle(21, "3k4/2p2pB1/p7/3N2p1/8/P7/PrP1QPPP/R4RK1 w - - 0 21", 2, "e2e7"),
            new Puzzle(22, "rr4k1/ppp2ppp/2n5/5p2/P2P1P2/2p1P1PP/2Pn4/2q2NK1 b - f3 0 20", 2, "c1f1"),
            new Puzzle(23, "3Q1Brk/6pp/4b3/4Pp2/7P/2N2P2/PPP4K/R3R3 w - - 1 27", 2, "f8g7"),
            new Puzzle(24, "r3k2r/ppp2pp1/6bp/8/1b1pP3/3K2P1/PqP4P/3Q1BR1 b kq - 1 19", 2, "b2c3"),
            new Puzzle(25, "rr4k1/1pB1Q3/pq2p1N1/3p3P/1P1P4/P2P3P/5P2/2RR2K1 w - - 3 37", 2, "h5h6"),
            new Puzzle(26, "r1bqkb1r/p3pppp/2p2n2/8/P4P2/1n4P1/1P3KBq/RNB5 b kq - 1 14", 2, "d8d1"),
            new Puzzle(27, "2b1kr2/2P4R/p4p2/3p4/3P4/8/PP3P1P/6RK w - - 7 35", 2, "g1e1"),
            new Puzzle(28, "4kr2/2p1bppp/2n4P/p5q1/3p4/3b4/5K2/8 b - - 2 29", 2, "g5g4"),
            new Puzzle(29, "7B/3pkp2/7p/3QN3/7P/r3P1P1/P3KP1R/R4B2 w - - 2 25", 2, "d5d7"),
            new Puzzle(30, "2k4r/p4p1p/7p/1P1pn3/p2R1br1/2n5/5P2/5R1K b - - 0 29", 2, "g4h4"),
            new Puzzle(31, "r1b2rk1/pppp1pbp/3p4/8/4B3/2NP4/PBP2PQP/R2K3R w - - 1 16", 2, "h1g1"),
            new Puzzle(32, "r4rk1/p3bppp/8/1p1np3/1q4P1/8/K7/8 b - - 9 37", 2, "d5c3"),
            new Puzzle(33, "k7/8/4P3/1Q6/PPBN2P1/8/5PP1/R4RK1 w - - 1 44", 2, "c4d5"),
            new Puzzle(34, "r3k1r1/1pp2pp1/3bp3/1N2n2p/3P4/1P6/P1P4q/R3KB2 b q - 1 20", 2, "e5f3"),
            new Puzzle(35, "Nk6/p4N2/4p3/2P5/1p2Qp2/7P/PP3PP1/R4RK1 w - - 0 25", 2, "f7d6"),
            new Puzzle(36, "5rk1/p1p2pp1/p6p/2p1p3/2P4P/2n1N1r1/P7/K7 b - - 0 36", 2, "f8b8"),
            new Puzzle(37, "k4b1Q/ppp4p/2N3p1/8/1P2r3/4B2P/P1P2P1P/R4RK1 w - - 1 23", 2, "h8f8"),
            new Puzzle(38, "1k6/2p3p1/p5p1/1p6/5b2/1Pn3r1/P1PN1K2/7r b - - 1 37", 2, "c3d1"),
            new Puzzle(39, "2Q5/7k/4B3/4P3/4P3/2B4p/P1P2P1P/RK3R2 w - - 1 40", 2, "f1g1"),
            new Puzzle(40, "5rk1/p1n2ppp/2q2n2/4p3/6P1/2N2P2/Pr5P/3R1K1R b - g3 0 22", 2, "c6f3"),
        };

        public static Puzzle? Find(int number) => All.FirstOrDefault(p => p.Number == number);
    }
}
