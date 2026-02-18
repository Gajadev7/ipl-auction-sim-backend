using AuctionEngine;

namespace AuctionServer.Contracts;

public class StartAuctionRequest
{
    public List<Team> Teams { get; init; } = [];

    public List<Player> Players { get; init; } = [];
}

public class PlaceBidRequest
{
    public Guid TeamId { get; init; }

    public decimal Amount { get; init; }
}

public class PassRequest
{
    public Guid TeamId { get; init; }
}
