using System;
using System.Linq;
using Chess.Core;
using Xunit;

namespace Chess.Core.Tests;

public class RewardTests
{
    [Fact]
    public void EveryPuzzleHasExactlyOneMatingFirstMove()
    {
        Assert.Equal(40, PuzzleBook.All.Count);
        Assert.Equal(PuzzleBook.All.Count, PuzzleBook.All.Select(p => p.Number).Distinct().Count());
        foreach (var puzzle in PuzzleBook.All)
        {
            var start = puzzle.Start;
            var mates = MateSolver.MatingMoves(start, puzzle.MateIn);
            Assert.Single(mates);
            Assert.Equal(puzzle.Solution, mates[0].ToUci());
            if (puzzle.MateIn > 1)
                Assert.Empty(MateSolver.MatingMoves(start, puzzle.MateIn - 1));
        }
    }

    [Fact]
    public void MateInTwoDefenceStillLosesToTheRightMove()
    {
        var puzzle = PuzzleBook.All.First(p => p.MateIn == 2);
        var start = puzzle.Start;
        var first = MoveGenerator.GenerateLegal(start).First(m => m.ToUci() == puzzle.Solution);
        var afterFirst = start.Apply(first);
        var defence = MateSolver.BestDefence(afterFirst, 1);
        Assert.NotNull(defence);
        var afterDefence = afterFirst.Apply(defence!.Value);
        Assert.NotEmpty(MateSolver.MatingMoves(afterDefence, 1));
    }

    [Fact]
    public void DailyQuestsStayTheSameAllDayAndChangeNextDay()
    {
        var progress = new PlayerProgress();
        Assert.True(Rewards.RefreshDaily(progress, new DateTime(2026, 10, 20), "abc"));
        var first = progress.daily.Select(d => d.id).ToList();
        Assert.Equal(3, first.Distinct().Count());
        Assert.False(Rewards.RefreshDaily(progress, new DateTime(2026, 10, 20, 23, 0, 0), "abc"));
        Assert.Equal(first, progress.daily.Select(d => d.id));
        Rewards.RefreshDaily(progress, new DateTime(2026, 10, 21), "abc");
        Assert.Equal("2026-10-21", progress.questDay);
    }

    [Fact]
    public void GamesCountForQuestsAndAchievements()
    {
        var progress = new PlayerProgress();
        progress.questDay = "x";
        progress.daily.Add(new QuestState { id = "d_win" });
        progress.daily.Add(new QuestState { id = "d_capture" });
        Rewards.Record(progress, new GameReport { Mode = "classic", Won = true, Bot = BotLevel.Hard, Captures = 7 });

        var win = Rewards.Quest("d_win")!;
        Assert.True(Rewards.CanClaim(progress, win));
        Assert.True(Rewards.Claim(progress, win));
        Assert.False(Rewards.Claim(progress, win));
        Assert.Equal(1, progress.chests[(int)ChestType.Common]);
        Assert.Equal(7, Rewards.Progress(progress, Rewards.Quest("d_capture")!));

        var hard = Rewards.Quest("a_hard")!;
        Assert.True(Rewards.Claim(progress, hard));
        Assert.Equal(1, progress.chests[(int)ChestType.Rare]);
        Assert.False(Rewards.CanClaim(progress, Rewards.Quest("a_online5")!));
    }

    [Fact]
    public void PuzzlesPayOnlyTheFirstTime()
    {
        var progress = new PlayerProgress();
        var puzzle = PuzzleBook.All[0];
        Assert.Equal(puzzle.Reward, Rewards.RecordPuzzle(progress, puzzle));
        Assert.Equal(0, Rewards.RecordPuzzle(progress, puzzle));
        Assert.Equal(1, progress.Total(Goal.Puzzles));
    }

    [Fact]
    public void ChestsGiveNewSkinsAndTheThirtiethIsLegendary()
    {
        var progress = new PlayerProgress();
        var random = new Random(7);
        int legendaries = 0;
        progress.chests[(int)ChestType.Common] = 30;
        for (int i = 0; i < 30; i++)
        {
            var result = Rewards.Open(progress, ChestType.Common, random)!;
            if (result.Skin.Rarity == Rarity.Legendary)
                legendaries++;
            if (i == 29 && legendaries == 0)
                Assert.Fail("The 30th chest without a legendary must give one.");
        }
        Assert.True(legendaries >= 1);
        Assert.Null(Rewards.Open(progress, ChestType.Common, random));
        Assert.Equal(progress.owned.Count, progress.owned.Distinct().Count());
    }

    [Fact]
    public void PityGuaranteesALegendary()
    {
        var progress = new PlayerProgress { sinceLegendary = Rewards.Pity - 1 };
        progress.chests[(int)ChestType.Common] = 1;
        Assert.Equal(Rarity.Legendary, Rewards.Open(progress, ChestType.Common, new Random(1))!.Skin.Rarity);
        Assert.Equal(0, progress.sinceLegendary);
    }

    [Fact]
    public void FullCollectionTurnsDuplicatesIntoCoins()
    {
        var progress = new PlayerProgress();
        progress.owned.AddRange(SkinCatalog.All.Select(s => s.Id));
        progress.chests[(int)ChestType.Epic] = 1;
        var result = Rewards.Open(progress, ChestType.Epic, new Random(3))!;
        Assert.True(result.Duplicate);
        Assert.True(progress.coins > 0);
    }

    [Fact]
    public void ShopAndEquipFollowTheRules()
    {
        var progress = new PlayerProgress { coins = 130 };
        Assert.False(Rewards.Buy(progress, ChestType.Rare));
        Assert.True(Rewards.Buy(progress, ChestType.Common));
        Assert.Equal(10, progress.coins);
        Assert.False(Rewards.Equip(progress, "rainbow"));
        progress.owned.Add("rainbow");
        Assert.True(Rewards.Equip(progress, "rainbow"));
        Assert.Equal("rainbow", progress.pieces);
        Assert.True(Rewards.Equip(progress, SkinCatalog.DefaultBoard));
    }
}
