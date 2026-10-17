using Chess.Core;

namespace Chess.Server;

/// <summary>One online game between two connected players. The server checks every action here.</summary>
public sealed class Match
{
    private readonly Game? _game;
    private readonly TimeTravelGame? _timeTravel;

    public Match(string mode, IPlayer white, IPlayer black, Army? whiteArmy, Army? blackArmy)
    {
        Mode = mode;
        White = white;
        Black = black;
        WhiteArmy = whiteArmy;
        BlackArmy = blackArmy;
        if (mode == Modes.TimeTravel)
            _timeTravel = new TimeTravelGame();
        else if (mode == Modes.Army)
            _game = new Game(Army.ToPosition(whiteArmy!, blackArmy!));
        else
            _game = new Game();
    }

    public string Mode { get; }
    public IPlayer White { get; }
    public IPlayer Black { get; }
    public Army? WhiteArmy { get; }
    public Army? BlackArmy { get; }
    public bool Finished { get; set; }

    public PieceColor SideToMove => _timeTravel?.Current.SideToMove ?? _game!.Current.SideToMove;
    public GameStatus Status => _timeTravel?.Status ?? _game!.Status;
    public PieceColor? Winner => _timeTravel?.Winner ?? _game!.Winner;

    public IPlayer Opponent(IPlayer player) => player == White ? Black : White;
    public PieceColor ColorOf(IPlayer player) => player == White ? PieceColor.White : PieceColor.Black;

    /// <summary>Plays the action if it is legal; returns false otherwise and changes nothing.</summary>
    public bool TryPlay(string action)
    {
        if (_timeTravel != null)
            return _timeTravel.TryPlay(action);
        if (_game!.IsOver)
            return false;
        var move = _game.FindMove(action);
        if (!move.HasValue)
            return false;
        _game.MakeMove(move.Value);
        return true;
    }
}

public static class Modes
{
    public const string Classic = "classic";
    public const string Army = "army";
    public const string TimeTravel = "timetravel";

    public static bool IsKnown(string? mode) => mode == Classic || mode == Army || mode == TimeTravel;
}
