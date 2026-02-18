using AuctionEngine;

namespace AuctionServer.Services;

public sealed class AiBidder
{
    private readonly Random _random;
    private readonly Dictionary<PlayerRole, decimal> _rolePreference;
    private bool _battleMode;

    public AiBidder(Guid teamId, string teamName, int seed)
    {
        TeamId = teamId;
        TeamName = teamName;

        _random = new Random(seed);
        AggressionFactor = 0.45m + (decimal)_random.NextDouble() * 0.45m;

        _rolePreference = new Dictionary<PlayerRole, decimal>
        {
            [PlayerRole.Batsman] = 0.75m + (decimal)_random.NextDouble() * 0.65m,
            [PlayerRole.Bowler] = 0.75m + (decimal)_random.NextDouble() * 0.65m,
            [PlayerRole.AllRounder] = 0.75m + (decimal)_random.NextDouble() * 0.65m,
            [PlayerRole.WicketKeeper] = 0.75m + (decimal)_random.NextDouble() * 0.65m
        };
    }

    public Guid TeamId { get; }

    public string TeamName { get; }

    public decimal AggressionFactor { get; }

    public void Observe(AuctionEvent auctionEvent)
    {
        switch (auctionEvent)
        {
            case PlayerNominatedEvent:
                _battleMode = _random.NextDouble() < (0.12 + (double)AggressionFactor * 0.22);
                break;
            case BidPlacedEvent bidPlacedEvent:
                if (bidPlacedEvent.TeamId != TeamId && _random.NextDouble() < 0.18)
                {
                    _battleMode = true;
                }

                break;
            case PlayerSoldEvent:
            case PlayerUnsoldEvent:
                _battleMode = false;
                break;
        }
    }

    public AiBidDecision Decide(Team team, AuctionManagerState state)
    {
        if (state.AuctionState != AuctionState.Bidding || state.CurrentPlayer is null)
        {
            return AiBidDecision.Hold();
        }

        if (state.CurrentHighestBidderTeamId == TeamId)
        {
            return AiBidDecision.Hold();
        }

        var currentPlayer = state.CurrentPlayer;
        var currentHighestBid = state.CurrentHighestBid ?? 0m;
        var minimumNextBid = currentHighestBid > 0m
            ? currentHighestBid + GetBidIncrement(currentHighestBid)
            : currentPlayer.BasePrice;

        if (team.PurseRemaining < minimumNextBid)
        {
            return AiBidDecision.Pass();
        }

        var roleWeight = _rolePreference[currentPlayer.Role];
        var battleBoost = _battleMode ? 1.20m + (decimal)_random.NextDouble() * 0.35m : 1m;
        var interestMultiplier = roleWeight * AggressionFactor * battleBoost;
        var interestCap = currentPlayer.BasePrice * (1m + interestMultiplier * 2.2m);
        var maxAffordableBid = Math.Min(team.PurseRemaining, interestCap);

        var contestThreshold = 0.35m + roleWeight * 0.30m + AggressionFactor * 0.25m;
        var contestRoll = (decimal)_random.NextDouble();

        if (contestRoll > contestThreshold && !_battleMode)
        {
            return AiBidDecision.Pass();
        }

        if (maxAffordableBid < minimumNextBid)
        {
            return AiBidDecision.Pass();
        }

        var bidAmount = minimumNextBid;
        if (_battleMode && _random.NextDouble() < 0.40)
        {
            var bonusRaise = GetBidIncrement(minimumNextBid) * _random.Next(1, 3);
            bidAmount += bonusRaise;
        }

        if (bidAmount > maxAffordableBid)
        {
            bidAmount = maxAffordableBid;
        }

        if (bidAmount <= currentHighestBid)
        {
            return AiBidDecision.Pass();
        }

        return AiBidDecision.Bid(bidAmount);
    }

    private static decimal GetBidIncrement(decimal currentBid)
    {
        if (currentBid < 200m)
        {
            return 10m;
        }

        if (currentBid < 800m)
        {
            return 25m;
        }

        return 50m;
    }
}

public readonly record struct AiBidDecision(AiBidDecisionType DecisionType, decimal Amount)
{
    public static AiBidDecision Hold()
    {
        return new AiBidDecision(AiBidDecisionType.Hold, 0m);
    }

    public static AiBidDecision Pass()
    {
        return new AiBidDecision(AiBidDecisionType.Pass, 0m);
    }

    public static AiBidDecision Bid(decimal amount)
    {
        return new AiBidDecision(AiBidDecisionType.Bid, amount);
    }
}

public enum AiBidDecisionType
{
    Hold,
    Pass,
    Bid
}
