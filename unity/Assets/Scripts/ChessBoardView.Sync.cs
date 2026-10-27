#nullable enable
using System.Collections.Generic;
using Chess.Core;
using UnityEngine;

/// <summary>
/// Keeps the player's progress on the server. Every change (a finished game, a claimed quest, a
/// chest) is a <see cref="ProgressAction"/>: the game applies it at once and queues it, and the
/// queue goes to the server one step at a time. The server applies the same steps, rolls chests
/// itself while it is reachable, and its copy replaces ours once the queue is empty. Without a
/// server everything works as before and the queue waits for the next connection.
/// </summary>
public partial class ChessBoardView
{
    [System.Serializable]
    private sealed class StepQueue
    {
        public List<ProgressAction> steps = new List<ProgressAction>();
        public int nextSeq = 1;
    }

    private const float ReconnectEvery = 30;

    private StepQueue _steps = new StepQueue();
    /// <summary>The connection the server answered our sync on; a new connection must sync again.</summary>
    private OnlineClient? _syncedOn;
    private bool _stepInFlight;
    private float _lastConnectTry = -100;
    private ChestType? _awaitingChest;
    private float _awaitingSince;

    /// <summary>The server has our progress and answers steps right now.</summary>
    private bool ProgressOnline => _net != null && _net.IsOpen && _syncedOn == _net;

    private void LoadSteps()
    {
        string json = PlayerPrefs.GetString("progressSteps", "");
        try
        {
            _steps = json.Length > 0 ? JsonUtility.FromJson<StepQueue>(json) ?? new StepQueue() : new StepQueue();
        }
        catch (System.ArgumentException)
        {
            _steps = new StepQueue();
        }
        _steps.steps ??= new List<ProgressAction>();
    }

    private void SaveSteps()
    {
        PlayerPrefs.SetString("progressSteps", JsonUtility.ToJson(_steps));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Applies a change to our copy (unless <paramref name="applied"/>: the caller already did) and
    /// queues it for the server.
    /// </summary>
    private ChestResult? Step(ProgressAction action, bool applied = false)
    {
        action.seq = _steps.nextSeq++;
        action.day = System.DateTime.Now.ToString("yyyy-MM-dd");
        ChestResult? result = null;
        if (!applied)
            result = ProgressSync.Apply(_progress, action, _playerId, System.DateTime.Now, new System.Random());
        _steps.steps.Add(action);
        SaveSteps();
        SaveProgress();
        SendNextStep();
        return result;
    }

    private void SendNextStep()
    {
        if (!ProgressOnline || _stepInFlight || _steps.steps.Count == 0)
            return;
        _stepInFlight = true;
        _net!.Send(new NetMessage { type = "progress", action = "step", data = JsonUtility.ToJson(_steps.steps[0]) });
    }

    /// <summary>Sent right after hello: our copy, which the server keeps only if it has none yet.</summary>
    private void SendProgressSync()
    {
        _syncedOn = null;
        _stepInFlight = false;
        _net?.Send(new NetMessage { type = "progress", action = "sync", data = JsonUtility.ToJson(_progress) });
    }

    /// <summary>The server's answer: drop the steps it has, and take its copy once nothing is left to send.</summary>
    private void ReceiveProgress(NetMessage message)
    {
        _syncedOn = _net;
        _stepInFlight = false;
        _steps.steps.RemoveAll(s => s.seq <= message.seq);
        // A fresh install starts counting at 1 again; carry on after what the server already has.
        if (_steps.nextSeq <= message.seq)
            _steps.nextSeq = message.seq + 1;
        SaveSteps();

        if (_awaitingChest.HasValue && message.skin.Length > 0)
        {
            var skin = SkinCatalog.Find(message.skin);
            if (skin != null)
            {
                _opened = new ChestResult(skin, message.duplicate, message.coins);
                _openedChest = _awaitingChest.Value;
                // The chest has been shaking while we waited; open it now.
                _openedAt = Time.time - 0.7f;
            }
            _awaitingChest = null;
        }

        if (_steps.steps.Count == 0 && message.data.Length > 0)
            AdoptServerProgress(message.data);
        SendNextStep();
    }

    private void AdoptServerProgress(string json)
    {
        PlayerProgress? server;
        try
        {
            server = JsonUtility.FromJson<PlayerProgress>(json);
        }
        catch (System.ArgumentException)
        {
            return;
        }
        if (server == null)
            return;
        ProgressSync.Repair(server);
        bool skinsChanged = server.pieces != _progress.pieces || server.board != _progress.board;
        _progress = server;
        Rewards.RefreshDaily(_progress, System.DateTime.Now, _playerId);
        SaveProgress();
        if (skinsChanged)
            ApplySkins();
    }

    private void ProgressConnectionLost()
    {
        _syncedOn = null;
        _stepInFlight = false;
        if (_awaitingChest.HasValue)
        {
            _awaitingChest = null;
            ShowToast("Связь с сервером пропала, сундук не открылся. Попробуйте ещё раз.");
        }
    }

    /// <summary>Connects in the background now and then, so queued steps reach the server.</summary>
    private void UpdateSync()
    {
        if (_awaitingChest.HasValue && Time.time - _awaitingSince > 15)
        {
            _awaitingChest = null;
            ShowToast("Сервер не ответил, сундук не открылся. Попробуйте ещё раз.");
        }
        if (_net != null || Time.time - _lastConnectTry < ReconnectEvery || _playerId.Length == 0)
            return;
        _lastConnectTry = Time.time;
        try
        {
            // Hello, the sync and the queue follow when the server says welcome.
            _net = new OnlineClient(_serverUrl.Trim());
        }
        catch (System.Exception)
        {
            // A broken address: the online screen tells the player when they go there.
        }
    }

    /// <summary>
    /// Opens a chest: with the server on line it rolls the chest and the result arrives in its
    /// answer; without one the game rolls and the server is told later.
    /// </summary>
    private void OpenChest(ChestType chest)
    {
        if (ProgressOnline)
        {
            _awaitingChest = chest;
            _awaitingSince = Time.time;
            Step(new ProgressAction { kind = ProgressAction.Open, number = (int)chest }, applied: true);
            Sfx.Play(Sfx.Jump);
            return;
        }
        var result = Rewards.Open(_progress, chest, new System.Random());
        if (result == null)
            return;
        Step(new ProgressAction { kind = ProgressAction.Open, number = (int)chest, skin = result.Skin.Id }, applied: true);
        _opened = result;
        _openedChest = chest;
        _openedAt = Time.time;
        Sfx.Play(Sfx.Jump);
    }

    private void DrawAwaitingChest(float width, float height)
    {
        Dim(width, height, 0.75f);
        float age = Time.time - _awaitingSince;
        float shake = Mathf.Sin(age * 60) * Mathf.Min(age, 0.7f) * 10;
        DrawChestIcon(new Rect(width / 2 - 70 + shake, height / 2 - 60, 140, 110), ChestColor(_awaitingChest!.Value), Mathf.Min(age, 0.7f) * 12);
        GUI.Label(new Rect(0, height / 2 + 70, width, 30), "Открываем...", Theme.Label(18, Theme.Muted, TextAnchor.MiddleCenter));
    }
}
