namespace AuctionEngine;

public class Bid
{
    public Guid TeamId { get; set; }

    public decimal Amount { get; set; }

    public DateTimeOffset Timestamp { get; set; }
}
