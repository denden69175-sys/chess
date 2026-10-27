using Chess.Core;

namespace Chess.Server;

/// <summary>
/// Checks what the game says about a finished game or a solved puzzle by replaying its moves
/// with the same rules. Only what the replay shows counts: who won, how many pieces were taken.
/// </summary>
public static class GameCheck
{
    /// <summary>The longest game we replay; real games are far shorter.</summary>
    public const int MaxActions = 2000;

    /// <summary>
    /// Replays a game step. Returns the step as the server counts it (result, captures and
    /// promotions from the replay), or null when a move is illegal or the game is too short to count.
    /// </summary>
    public static ProgressAction? Verify(ProgressAction action)
    {
        if (!Modes.IsKnown(action.mode))
            return null;
        int players = Modes.PlayersIn(action.mode);
        if (action.seat < -1 || action.seat >= players)
            return null;
        Army? white = null, black = null;
        if (action.mode == Modes.Army)
        {
            white = Army.FromCode(action.whiteArmy);
            black = Army.FromCode(action.blackArmy);
            if (white == null || black == null || !white.IsValid || !black.IsValid)
                return null;
        }

        var seats = Enumerable.Range(0, players).Select(_ => (IPlayer)new Nobody()).ToList();
        var match = new Match(action.mode, seats, white, black);
        var lines = (action.actions ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > MaxActions)
            return null;
        foreach (var line in lines)
            if (!match.TryPlay(line.Trim()))
                return null;

        int won;
        if (match.IsOver)
        {
            won = WonBy(match, action.seat);
        }
        else
        {
            // Given up (by either side), or out of "Тройной трон": nobody's win, since the moves
            // cannot show who gave up. Only after a few moves, so that starting and dropping
            // games does not fill the "play games" quests.
            if (lines.Length < players * 2)
                return null;
            won = 0;
        }

        return new ProgressAction
        {
            seq = action.seq,
            kind = ProgressAction.Game,
            day = action.day,
            mode = action.mode,
            won = won,
            bot = action.seat >= 0 ? action.bot : -1,
            online = false,
            captures = match.CapturesBy(action.seat),
            promotions = match.PromotionsBy(action.seat),
        };
    }

    /// <summary>1 when the seat won the finished match, -1 when it lost, 0 for a draw or no seat.</summary>
    public static int WonBy(Match match, int seat)
    {
        string result = match.Result;
        if (seat < 0 || result == "draw")
            return 0;
        if (result.StartsWith("seat"))
            return result == "seat" + seat ? 1 : -1;
        var winner = result == "white" ? PieceColor.White : PieceColor.Black;
        return match.ColorOf(seat) == winner ? 1 : -1;
    }

    /// <summary>
    /// True when the moves (attacker and defender in turn, UCI) are legal from the puzzle's
    /// position and end in mate within the puzzle's number of moves.
    /// </summary>
    public static bool VerifyPuzzle(ProgressAction action)
    {
        var puzzle = PuzzleBook.Find(action.number);
        if (puzzle == null)
            return false;
        var lines = (action.actions ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || lines.Length > puzzle.MateIn * 2 - 1)
            return false;
        var game = new Game(puzzle.Start);
        foreach (var line in lines)
        {
            var move = game.FindMove(line.Trim());
            if (!move.HasValue)
                return false;
            game.MakeMove(move.Value);
        }
        return MateSolver.IsCheckmate(game.Current);
    }

    private sealed class Nobody : IPlayer
    {
        public string Name { get; set; } = "";
        public string? Id { get; set; }
        public void Send(Message message)
        {
        }
    }
}
