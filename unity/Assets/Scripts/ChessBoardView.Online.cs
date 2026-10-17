#nullable enable
using Chess.Core;
using UnityEngine;

/// <summary>Online play: the online screen, the connection to the server and its messages.</summary>
public partial class ChessBoardView
{
    private const string DefaultServer = "ws://localhost:5080/ws";

    private static readonly string[] OnlineModeNames = { "Классика", "Своя армия", "Время" };
    private static readonly string[] OnlineModeCodes = { "classic", "army", "timetravel" };

    private enum NetState
    {
        Idle,
        Connecting,
        Searching,
        Room,
    }

    private OnlineClient? _net;
    private NetState _netState = NetState.Idle;
    private NetMessage? _pendingRequest;
    private string _roomCode = "";
    private string _joinCode = "";
    private string _playerName = "Игрок";
    private string _serverUrl = DefaultServer;
    private bool _showServer;
    private int _onlineMode;

    private string _opponentName = "";
    private bool _waitingForServer;
    private bool _onlineEnded;
    private PieceColor? _onlineWinner;
    private string _onlineReason = "";

    // ---------------------------------------------------------------- connection

    private void Request(NetMessage request)
    {
        _pendingRequest = request;
        _netState = NetState.Connecting;
        if (_net != null && _net.Url != _serverUrl.Trim())
        {
            _net.Dispose();
            _net = null;
        }
        if (_net == null)
        {
            try
            {
                _net = new OnlineClient(_serverUrl.Trim());
            }
            catch (System.Exception e)
            {
                _netState = NetState.Idle;
                ShowToast("Неверный адрес сервера: " + e.Message);
            }
            // The request goes out when the server says hello.
            return;
        }
        if (_net.IsOpen)
            SendPending();
    }

    private void SendPending()
    {
        if (_net == null)
            return;
        _net.Send(new NetMessage { type = "hello", name = _playerName });
        if (_pendingRequest != null)
        {
            _net.Send(_pendingRequest);
            _pendingRequest = null;
        }
    }

    private void SendAction(string action)
    {
        if (_net == null)
            return;
        _net.Send(new NetMessage { type = "move", action = action });
        _waitingForServer = true;
    }

    private void Resign()
    {
        _net?.Send(new NetMessage { type = "resign" });
    }

    private void CancelSearch()
    {
        if (_net != null && _net.IsOpen)
            _net.Send(new NetMessage { type = "cancel" });
        _pendingRequest = null;
        _netState = NetState.Idle;
    }

    private void PollNetwork()
    {
        while (_net != null && _net.TryReceive(out var message))
            HandleNet(message);
    }

    private void HandleNet(NetMessage message)
    {
        switch (message.type)
        {
            case "welcome":
                SendPending();
                break;
            case "waiting":
                _netState = NetState.Searching;
                break;
            case "room":
                _netState = NetState.Room;
                _roomCode = message.code;
                break;
            case "start":
                StartOnlineGame(message);
                break;
            case "move":
                _waitingForServer = false;
                if (_page == Page.Game && _opponent == Opponent.Online && !ApplyText(message.action))
                    ShowToast("Ход с сервера не подошёл к доске: " + message.action);
                break;
            case "end":
                EndOnlineGame(message.result, message.reason);
                break;
            case "error":
                _waitingForServer = false;
                if (_page == Page.Game)
                    RedrawPieces();
                else
                    _netState = NetState.Idle;
                ShowToast(message.message);
                break;
            case OnlineClient.Closed:
                _net?.Dispose();
                _net = null;
                if (_page == Page.Game && _opponent == Opponent.Online && !GameFinished)
                    EndOnlineGame("", "connection");
                else if (_netState != NetState.Idle)
                    ShowToast(message.message + "\nПроверьте, что сервер запущен и адрес верный.");
                _netState = NetState.Idle;
                _waitingForServer = false;
                break;
        }
    }

    private void StartOnlineGame(NetMessage message)
    {
        _netState = NetState.Idle;
        _opponent = Opponent.Online;
        _human = message.color == "black" ? PieceColor.Black : PieceColor.White;
        _timeTravel = message.mode == "timetravel";
        bool army = message.mode == "army";
        _whiteArmy = army ? Army.FromCode(message.whiteArmy) : null;
        _blackArmy = army ? Army.FromCode(message.blackArmy) : null;
        _botArmyName = "";
        _opponentName = message.name.Length > 0 ? message.name : "Соперник";
        StartGame();
        _onlineEnded = false;
        _waitingForServer = false;
        ShowToast($"Соперник найден: {_opponentName}. Вы играете {(_human == PieceColor.White ? "белыми" : "чёрными")}.");
    }

    private void EndOnlineGame(string result, string reason)
    {
        if (_onlineEnded || _page != Page.Game || _opponent != Opponent.Online)
            return;
        _onlineEnded = true;
        _onlineWinner = result == "white" ? PieceColor.White : result == "black" ? PieceColor.Black : (PieceColor?)null;
        _onlineReason = reason;
        _waitingForServer = false;
        // A checkmate already set the timer when the last move arrived.
        if (!_game.IsOver)
            _endAt = Time.time + 0.2f;
    }

    // ---------------------------------------------------------------- screen

    private void DrawOnline(float width, float height)
    {
        bool waiting = _netState != NetState.Idle;
        Theme.InputBlocked = waiting;
        if (BackButton())
        {
            Theme.InputBlocked = false;
            CancelSearch();
            return;
        }

        float cardWidth = Mathf.Min(580, width - 24);
        float inner = cardWidth - 48;
        bool army = _onlineMode == 1;
        float cardHeight = 470 + (army ? 44 : 0) + (_showServer ? 50 : 0);
        float x = (width - cardWidth) / 2;
        float y = Mathf.Max(74, (height - cardHeight) / 2);
        Theme.PanelBox(new Rect(x, y, cardWidth, cardHeight), 24);
        float cx = x + 24;
        float cy = y + 18;

        if (_pieceTextures.TryGetValue("bN", out var icon))
            GUI.DrawTexture(new Rect(cx, cy - 4, 56, 56), icon);
        GUI.Label(new Rect(cx + 66, cy, inner - 66, 48), "Онлайн", Theme.Label(30, Theme.Text, display: true));
        cy += 62;

        GUI.Label(new Rect(cx, cy, inner, 22), "Ваше имя", Theme.Label(16, Theme.Muted, bold: true));
        string name = Theme.TextField(new Rect(cx, cy + 24, inner, 44), _playerName, 20);
        if (name != _playerName)
        {
            _playerName = name;
            PlayerPrefs.SetString("name", name);
        }
        cy += 80;

        GUI.Label(new Rect(cx, cy, inner, 22), "Режим", Theme.Label(16, Theme.Muted, bold: true));
        _onlineMode = Theme.Pills(new Rect(cx, cy + 24, inner, 44), _onlineMode, OnlineModeNames, Theme.Teal);
        cy += 80;

        if (army)
        {
            var playerArmy = PlayerArmy();
            string text = playerArmy.IsValid ? $"Ваша армия: {playerArmy.Pieces.Count} фигур на {playerArmy.Cost} очков" : "Армия не готова";
            GUI.Label(new Rect(cx, cy, inner - 140, 36), text, Theme.Label(16, playerArmy.IsValid ? Theme.Text : Theme.Red));
            if (Theme.Button(new Rect(cx + inner - 130, cy, 130, 36), "Изменить", Theme.Orange, 15))
                ShowPage(Page.Army);
            cy += 44;
        }

        if (Theme.Button(new Rect(cx, cy, inner, 60), "НАЙТИ СОПЕРНИКА", Theme.Green, 22))
            StartRequest("find");
        cy += 74;

        if (Theme.Button(new Rect(cx, cy, inner / 2 - 6, 50), "Создать комнату", Theme.Blue, 17))
            StartRequest("create");
        _joinCode = Theme.TextField(new Rect(cx + inner / 2 + 6, cy, inner / 4 - 6, 50), _joinCode, 4, 22);
        if (Theme.Button(new Rect(cx + inner * 3 / 4 + 6, cy, inner / 4 - 6, 50), "Войти", Theme.Orange, 17, _joinCode.Trim().Length == 4))
            StartRequest("join");
        GUI.Label(new Rect(cx + inner / 2 + 6, cy + 52, inner / 2, 20), "код комнаты друга", Theme.Label(13, Theme.Muted));
        cy += 84;

        if (GUI.Button(new Rect(cx, cy, inner, 24), "Сервер: " + _serverUrl + (_showServer ? "   (скрыть)" : "   (изменить)"), Theme.Label(14, Theme.Muted)))
            _showServer = !_showServer;
        if (_showServer)
        {
            string url = Theme.TextField(new Rect(cx, cy + 28, inner, 40), _serverUrl, 200, 16);
            if (url != _serverUrl)
            {
                _serverUrl = url;
                PlayerPrefs.SetString("server", url);
            }
        }

        Theme.InputBlocked = false;
        if (waiting)
            DrawWaiting(width, height);
    }

    private void StartRequest(string type)
    {
        SaveMenuSettings();
        var request = new NetMessage { type = type, mode = OnlineModeCodes[_onlineMode] };
        if (type == "join")
            request.code = _joinCode.Trim();
        // The army goes along every time: the server uses it only in army games, including a
        // friend's army room joined by code.
        var army = PlayerArmy();
        if (_onlineMode == 1 && type != "join" && !army.IsValid)
        {
            ShowToast("Сначала соберите армию по правилам.");
            return;
        }
        if (army.IsValid)
            request.army = army.ToCode();
        Request(request);
    }

    private void DrawWaiting(float width, float height)
    {
        Dim(width, height);
        float cardWidth = Mathf.Min(440, width - 24);
        var card = new Rect((width - cardWidth) / 2, (height - 300) / 2, cardWidth, 300);
        Theme.Card(card, Theme.Darken(Theme.Teal, 0.55f), 24);

        if (_netState == NetState.Room)
        {
            GUI.Label(new Rect(card.x, card.y + 20, card.width, 30), "Код комнаты", Theme.Label(20, Theme.Muted, TextAnchor.MiddleCenter, bold: true));
            Theme.ShadowText(new Rect(card.x, card.y + 56, card.width, 80), _roomCode, Theme.Label(72, Theme.Gold, TextAnchor.MiddleCenter, display: true), 4);
            GUI.Label(new Rect(card.x + 20, card.y + 140, card.width - 40, 60), "Скажите код другу. Игра начнётся, когда он войдёт.",
                Theme.Label(17, Theme.Text, TextAnchor.MiddleCenter, wrap: true));
        }
        else
        {
            // A knight spins while we wait.
            var spinner = new Rect(card.x + (card.width - 80) / 2, card.y + 30, 80, 80);
            if (_pieceTextures.TryGetValue("wN", out var knight) && Event.current.type == EventType.Repaint)
            {
                var matrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(Mathf.Sin(Time.time * 3) * 25, spinner.center);
                GUI.DrawTexture(spinner, knight);
                GUI.matrix = matrix;
            }
            string dots = new string('.', 1 + (int)(Time.time * 2) % 3);
            string text = _netState == NetState.Connecting ? "Подключаемся к серверу" : "Ищем соперника";
            GUI.Label(new Rect(card.x, card.y + 128, card.width, 40), text + dots, Theme.Label(24, Theme.Text, TextAnchor.MiddleCenter, bold: true));
            GUI.Label(new Rect(card.x, card.y + 168, card.width, 26), "Режим: " + OnlineModeNames[_onlineMode],
                Theme.Label(16, Theme.Muted, TextAnchor.MiddleCenter));
        }

        if (Theme.Button(new Rect(card.x + (cardWidth - 200) / 2, card.yMax - 70, 200, 48), "Отмена", Theme.Grey, 18))
            CancelSearch();
    }
}
