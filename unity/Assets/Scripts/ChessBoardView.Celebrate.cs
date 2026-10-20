#nullable enable
using System.Collections.Generic;
using System.Linq;
using Chess.Core;
using UnityEngine;

/// <summary>
/// The coin counter that is always on screen, and the congratulation card with confetti that
/// appears when a quest is finished or a puzzle is solved.
/// </summary>
public partial class ChessBoardView
{
    private sealed class Celebration
    {
        public string Title = "";
        public string Text = "";
        public string Reward = "";
        /// <summary>The finished quest, so the card can hand out its reward at once.</summary>
        public QuestDef? Quest;
        public bool Puzzle;
    }

    private readonly Queue<Celebration> _celebrations = new Queue<Celebration>();
    private Celebration? _celebration;
    private float _celebrationAt;
    private float _celebrateAfter;
    private int _shownCoins = -1;
    private int _coinGain;
    private float _coinGainAt = -100;

    private bool CelebrationVisible => _celebration != null;

    /// <summary>Quests that can be claimed now, by id, to spot the ones a game or puzzle just finished.</summary>
    private HashSet<string> ClaimableIds()
    {
        Rewards.RefreshDaily(_progress, System.DateTime.Now, _playerId);
        return new HashSet<string>(DailyQuests().Concat(Rewards.Achievements).Where(q => Rewards.CanClaim(_progress, q)).Select(q => q.Id));
    }

    /// <summary>Queues a card for every quest that became claimable since <paramref name="before"/>.</summary>
    private void CelebrateNewQuests(HashSet<string> before)
    {
        foreach (var id in ClaimableIds().Where(id => !before.Contains(id)))
        {
            var quest = Rewards.Quest(id)!;
            bool achievement = Rewards.Achievements.Contains(quest);
            _celebrations.Enqueue(new Celebration
            {
                Title = achievement ? "ДОСТИЖЕНИЕ!" : "ЗАДАНИЕ ВЫПОЛНЕНО!",
                Text = quest.Text,
                Reward = "Награда: " + quest.Reward,
                Quest = quest,
            });
        }
    }

    private void CelebratePuzzle(Puzzle puzzle, int coins, bool firstSolve)
    {
        _celebrations.Enqueue(new Celebration
        {
            Title = "ЗАДАЧА РЕШЕНА!",
            Text = $"Задача {puzzle.Number}: мат в {puzzle.MateIn} {(puzzle.MateIn == 1 ? "ход" : "хода")}",
            Reward = coins > 0 ? $"+{coins} монет" : firstSolve ? "Монеты ушли на подсказки" : "Монеты за неё уже получены",
            Puzzle = true,
        });
    }

    /// <summary>Shows the next card when there is one. Cards wait while a game's result is still sliding in.</summary>
    private void UpdateCelebration()
    {
        if (_celebration == null && _celebrations.Count > 0 && Time.time >= _celebrateAfter)
        {
            _celebration = _celebrations.Dequeue();
            _celebrationAt = Time.time;
            Sfx.Play(Sfx.Win);
        }
    }

    private Rect CelebrationCard(float width, float height)
    {
        float cardWidth = Mathf.Min(480, width - 24);
        return new Rect((width - cardWidth) / 2, (height - 330) / 2, cardWidth, 330);
    }

    private (Rect Main, Rect Second) CelebrationButtons(Rect card)
    {
        float buttonWidth = (card.width - 60) / 2;
        return (new Rect(card.x + 20, card.yMax - 78, buttonWidth, 54), new Rect(card.x + 40 + buttonWidth, card.yMax - 78, buttonWidth, 54));
    }

    /// <summary>
    /// Takes clicks for the card before anything under it can, since IMGUI hands an event to the
    /// first control that asks. Called at the start of OnGUI.
    /// </summary>
    private void CelebrationInput(float width, float height)
    {
        var e = Event.current;
        if (_celebration == null || (e.type != EventType.MouseDown && e.type != EventType.MouseUp))
            return;
        if (e.type == EventType.MouseDown && e.button == 0 && Time.time - _celebrationAt > 0.5f)
        {
            var (main, second) = CelebrationButtons(CelebrationCard(width, height));
            if (main.Contains(e.mousePosition))
                CelebrationMain();
            else if (second.Contains(e.mousePosition))
                CloseCelebration();
        }
        Theme.ClickTaken();
        e.Use();
    }

    private void CelebrationMain()
    {
        var card = _celebration!;
        if (card.Quest != null)
        {
            if (Rewards.Claim(_progress, card.Quest))
            {
                SaveProgress();
                ShowToast("Получено: " + card.Quest.Reward + ".");
            }
        }
        else if (card.Puzzle && _page == Page.Puzzle)
        {
            var next = PuzzleBook.All.FirstOrDefault(p => p.Number == _puzzle.Number + 1);
            if (next != null)
                StartPuzzle(next);
        }
        CloseCelebration();
    }

    private void CloseCelebration()
    {
        _celebration = null;
        Sfx.Play(Sfx.Click);
    }

    private void DrawCelebration(float width, float height)
    {
        if (_celebration == null)
            return;
        var card = _celebration;
        float age = Time.time - _celebrationAt;
        float appear = Mathf.Clamp01(age / 0.3f);
        Dim(width, height, 0.55f * appear);
        DrawConfetti(width, height, age);

        var rect = CelebrationCard(width, height);
        float pop = 1 + Mathf.Sin(Mathf.Clamp01(age / 0.35f) * Mathf.PI) * 0.06f;
        rect = new Rect(rect.center.x - rect.width * pop / 2, rect.center.y - rect.height * pop / 2, rect.width * pop, rect.height * pop);
        var accent = card.Puzzle ? PuzzleColor : Theme.Gold;
        GUI.color = new Color(1, 1, 1, appear);
        Theme.Card(rect, Theme.Darken(accent, 0.55f), 26);

        // A trophy: a king in a glowing circle.
        float glow = 96 + Mathf.Sin(Time.time * 4) * 6;
        var center = new Vector2(rect.center.x, rect.y + 78);
        Theme.Fill(new Rect(center.x - glow / 2, center.y - glow / 2, glow, glow), Theme.WithAlpha(accent, 0.35f), glow / 2);
        if (_pieceTextures.TryGetValue(card.Puzzle ? "wK" : "wQ", out var trophy))
            GUI.DrawTexture(new Rect(center.x - 36, center.y - 38, 72, 72), trophy);

        Theme.ShadowText(new Rect(rect.x, rect.y + 128, rect.width, 46), card.Title,
            Theme.Label(card.Title.Length > 14 ? 26 : 32, accent, TextAnchor.MiddleCenter, display: true), 3);
        GUI.Label(new Rect(rect.x + 20, rect.y + 176, rect.width - 40, 30), card.Text, Theme.Label(19, Theme.Text, TextAnchor.MiddleCenter, bold: true, wrap: true));
        GUI.Label(new Rect(rect.x + 20, rect.y + 208, rect.width - 40, 28), card.Reward, Theme.Label(17, Theme.Gold, TextAnchor.MiddleCenter));

        var (main, second) = CelebrationButtons(CelebrationCard(width, height));
        string mainText = card.Quest != null ? "Забрать" : card.Puzzle && _page == Page.Puzzle && PuzzleBook.All.Any(p => p.Number == _puzzle.Number + 1) ? "Следующая" : "Ура!";
        Theme.InputBlocked = false;
        Theme.Button(main, mainText, Theme.Green, 19);
        Theme.Button(second, card.Quest != null ? "Позже" : "Закрыть", Theme.Blue, 19);
        GUI.color = Color.white;
    }

    private static void DrawConfetti(float width, float height, float age)
    {
        if (age > 4)
            return;
        Color[] colors = { Theme.Gold, Theme.Red, Theme.Green, Theme.Blue, Theme.Purple, Theme.Teal, Theme.Orange };
        for (int i = 0; i < 70; i++)
        {
            // Each piece has its own fixed start, speed and sway, so no state is needed.
            float seed = i * 12.9898f;
            float x0 = Mathf.Repeat(Mathf.Sin(seed) * 43758.5453f, 1) * width;
            float speed = 140 + Mathf.Repeat(Mathf.Sin(seed * 1.7f) * 9631.13f, 1) * 220;
            float delay = Mathf.Repeat(Mathf.Sin(seed * 2.3f) * 5137.7f, 1) * 0.6f;
            float t = age - delay;
            if (t < 0)
                continue;
            float y = -20 + t * speed;
            if (y > height)
                continue;
            float x = x0 + Mathf.Sin(t * 3 + i) * 24;
            float size = 7 + i % 4 * 2;
            var color = colors[i % colors.Length];
            color.a = Mathf.Clamp01(4 - age);
            Theme.Fill(new Rect(x, y, size, size * (0.5f + Mathf.Abs(Mathf.Sin(t * 6 + i)) * 0.8f)), color, 2);
        }
    }

    /// <summary>The coin counter in the corner of every screen; it pulses and shows "+N" when coins come in.</summary>
    private void DrawCoins(float width, float height)
    {
        int coins = _progress.coins;
        if (_shownCoins < 0)
            _shownCoins = coins;
        if (coins != _shownCoins)
        {
            if (coins > _shownCoins)
            {
                _coinGain = coins - _shownCoins;
                _coinGainAt = Time.time;
            }
            _shownCoins = coins;
        }

        string text = coins.ToString();
        var style = Theme.Label(18, Theme.Gold, TextAnchor.MiddleLeft, bold: true);
        float textWidth = style.CalcSize(new GUIContent(text)).x;
        float since = Time.time - _coinGainAt;
        float pulse = since < 0.6f ? 1 + Mathf.Sin(since / 0.6f * Mathf.PI) * 0.15f : 1;
        var pill = new Rect(12, height - 50, 52 + textWidth, 38);
        pill = new Rect(pill.x, pill.center.y - pill.height * pulse / 2, pill.width * pulse, pill.height * pulse);
        Theme.Fill(pill, new Color(0.05f, 0.04f, 0.12f, 0.85f), pill.height / 2);
        Theme.Outline(pill, Theme.WithAlpha(Theme.Gold, 0.7f), 2, pill.height / 2);

        // A coin: a gold disc with a darker rim and a shine.
        var coin = new Rect(pill.x + 7, pill.center.y - 12, 24, 24);
        Theme.Fill(coin, Theme.Darken(Theme.Gold, 0.25f), 12);
        Theme.Fill(new Rect(coin.x + 3, coin.y + 3, 18, 18), Theme.Gold, 9);
        Theme.Fill(new Rect(coin.x + 7, coin.y + 6, 5, 5), Theme.WithAlpha(Color.white, 0.7f), 2.5f);
        GUI.Label(new Rect(coin.xMax + 8, pill.y, textWidth + 10, pill.height), text, style);

        if (since < 1.6f)
        {
            float rise = since * 30;
            var gain = Theme.Label(18, Theme.WithAlpha(Theme.Gold, Mathf.Clamp01(1.6f - since)), TextAnchor.MiddleLeft, bold: true);
            GUI.Label(new Rect(pill.xMax + 8, pill.y - rise, 80, pill.height), "+" + _coinGain, gain);
        }
    }
}
