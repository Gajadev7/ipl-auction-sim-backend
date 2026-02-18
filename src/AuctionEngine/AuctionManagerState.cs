namespace AuctionEngine;

public class AuctionManagerState
{
    public AuctionState AuctionState { get; init; }

    public IReadOnlyList<Team> Teams { get; init; } = [];

    public IReadOnlyList<Player> RemainingPlayers { get; init; } = [];

    public Player? CurrentPlayer { get; init; }

    public decimal? CurrentHighestBid { get; init; }

    public Guid? CurrentHighestBidderTeamId { get; init; }
}
