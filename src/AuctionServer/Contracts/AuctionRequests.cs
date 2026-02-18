namespace AuctionServer.Contracts;

public class PlaceBidRequest
{
    public Guid TeamId { get; init; }

    public decimal Amount { get; init; }
}

public class PassRequest
{
    public Guid TeamId { get; init; }
}
