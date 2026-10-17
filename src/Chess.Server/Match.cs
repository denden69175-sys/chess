using Chess.Core;

namespace Chess.Server;

/// <summary>One online game between two connected players. The server checks every action here.</summary>
public sealed class Match
{
    private readonly Game? _game;
    private readonly TimeTravelGame? _timeTravel;
    private MultiverseGame? _multiverse;

    public Match(string mode, IPlayer white, IPlayer black, Army? whiteArmy, Army? blackArmy)
    {
        Mode = mode;
        White = white;
        Black = black;
        WhiteArmy = whiteArmy;
        BlackArmy = blackArmy;
        if (mode == Modes.TimeTravel)
            _timeTravel = new TimeTravelGame();
        else if (mode == Modes.FiveD)
            _multiverse = new MultiverseGame();
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

    public PieceColor SideToMove => _multiverse?.Mover ?? _timeTravel?.Current.SideToMove ?? _game!.Current.SideToMove;

    public GameStatus Status => _multiverse == null ? _timeTravel?.Status ?? _game!.Status : _multiverse.Result switch
    {
        MultiverseResult.Checkmate => GameStatus.Checkmate,
        MultiverseResult.Stalemate => GameStatus.Stalemate,
        _ => GameStatus.Ongoing,
    };

    public PieceColor? Winner => _multiverse != null ? _multiverse.Winner : _timeTravel?.Winner ?? _game!.Winner;

    public IPlayer Opponent(IPlayer player) => player == White ? Black : White;
    public PieceColor ColorOf(IPlayer player) => player == White ? PieceColor.White : PieceColor.Black;

    /// <summary>Plays the action if it is legal; returns false otherwise and changes nothing.</summary>
    public bool TryPlay(string action)
    {
        if (_multiverse != null)
            return PlayTurn(action);
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

    /// <summary>
    /// In 5D chess one action is a whole turn: its moves separated by ';', for example
    /// "0,2,f3>0,0,h3;1,1,e7>1,1,e5". The turn ends at once, so a half-made turn never reaches the other player.
    /// </summary>
    private bool PlayTurn(string action)
    {
        var copy = _multiverse!.Clone();
        foreach (var part in action.Split(';'))
            if (part == "submit" || !copy.TryPlay(part))
                return false;
        if (!copy.TryPlay("submit"))
            return false;
        _multiverse = copy;
        return true;
    }
}

public static class Modes
{
    public const string Classic = "classic";
    public const string Army = "army";
    public const string TimeTravel = "timetravel";
    public const string FiveD = "5d";

    public static bool IsKnown(string? mode) => mode == Classic || mode == Army || mode == TimeTravel || mode == FiveD;
}
