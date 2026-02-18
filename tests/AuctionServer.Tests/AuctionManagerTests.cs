using AuctionEngine;
using Xunit;

namespace AuctionServer.Tests;

public class AuctionManagerTests
{
    [Fact]
    public void CannotBidBelowCurrentPrice()
    {
        var (manager, teamA, teamB, _) = CreateStartedAuction();

        manager.PlaceBid(teamA.Id, 200m);

        Assert.Throws<InvalidOperationException>(() => manager.PlaceBid(teamB.Id, 150m));
    }

    [Fact]
    public void CannotBidWithoutPurse()
    {
        var (manager, teamA, _, _) = CreateStartedAuction(teamAPurse: 100m, teamBPurse: 1_000m);

        Assert.Throws<InvalidOperationException>(() => manager.PlaceBid(teamA.Id, 150m));
    }

    [Fact]
    public void PlayerSoldToHighestBidder()
    {
        var (manager, teamA, teamB, player) = CreateStartedAuction();

        manager.PlaceBid(teamB.Id, 300m);
        manager.Pass(teamA.Id);
        manager.FinalizeSale();

        var state = manager.GetCurrentState();

        Assert.Equal(AuctionState.Sold, state.AuctionState);
        Assert.Equal(300m, state.CurrentPlayer?.SoldPrice);
        Assert.Equal(teamB.Id, state.CurrentPlayer?.SoldToTeamId);
        Assert.Equal(player.Id, state.CurrentPlayer?.Id);
    }

    [Fact]
    public void PlayerUnsoldIfNoBids()
    {
        var (manager, teamA, teamB, _) = CreateStartedAuction();

        manager.Pass(teamA.Id);
        manager.Pass(teamB.Id);
        manager.FinalizeSale();

        var state = manager.GetCurrentState();

        Assert.Equal(AuctionState.Unsold, state.AuctionState);
        Assert.Null(state.CurrentPlayer?.SoldPrice);
        Assert.Null(state.CurrentPlayer?.SoldToTeamId);
    }

    [Fact]
    public void PurseDeductedCorrectly()
    {
        var (manager, teamA, teamB, _) = CreateStartedAuction(teamAPurse: 1_000m, teamBPurse: 1_000m);

        manager.PlaceBid(teamB.Id, 275m);
        manager.Pass(teamA.Id);
        manager.FinalizeSale();

        var state = manager.GetCurrentState();
        var winner = state.Teams.Single(team => team.Id == teamB.Id);

        Assert.Equal(725m, winner.PurseRemaining);
    }

    [Fact]
    public void SquadUpdatedCorrectly()
    {
        var (manager, teamA, teamB, player) = CreateStartedAuction();

        manager.PlaceBid(teamB.Id, 220m);
        manager.Pass(teamA.Id);
        manager.FinalizeSale();

        var state = manager.GetCurrentState();
        var winner = state.Teams.Single(team => team.Id == teamB.Id);

        Assert.Single(winner.Squad);
        Assert.Equal(player.Id, winner.Squad[0].Id);
        Assert.Equal(teamB.Id, winner.Squad[0].SoldToTeamId);
    }

    private static (AuctionManager Manager, Team TeamA, Team TeamB, Player Player) CreateStartedAuction(
        decimal teamAPurse = 1_000m,
        decimal teamBPurse = 1_000m)
    {
        var manager = new AuctionManager();
        var teamA = CreateTeam("Team A", teamAPurse);
        var teamB = CreateTeam("Team B", teamBPurse);
        var player = CreatePlayer("Player 1", PlayerRole.Batsman, 100m);

        manager.StartAuction([teamA, teamB], [player]);
        manager.NominateNextPlayer();

        return (manager, teamA, teamB, player);
    }

    private static Team CreateTeam(string name, decimal purseRemaining)
    {
        return new Team
        {
            Id = Guid.NewGuid(),
            Name = name,
            PurseRemaining = purseRemaining,
            Squad = []
        };
    }

    private static Player CreatePlayer(string name, PlayerRole role, decimal basePrice)
    {
        return new Player
        {
            Id = Guid.NewGuid(),
            Name = name,
            Role = role,
            BasePrice = basePrice
        };
    }
}
