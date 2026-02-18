namespace AuctionEngine;

public abstract class AuctionEvent
{
    protected AuctionEvent(DateTimeOffset occurredAt)
    {
        OccurredAt = occurredAt;
    }

    public DateTimeOffset OccurredAt { get; }
}

public class AuctionStartedEvent : AuctionEvent
{
    public AuctionStartedEvent(int teamCount, int playerCount, DateTimeOffset occurredAt)
        : base(occurredAt)
    {
        TeamCount = teamCount;
        PlayerCount = playerCount;
    }

    public int TeamCount { get; }

    public int PlayerCount { get; }
}

public class PlayerNominatedEvent : AuctionEvent
{
    public PlayerNominatedEvent(Guid playerId, string playerName, decimal basePrice, DateTimeOffset occurredAt)
        : base(occurredAt)
    {
        PlayerId = playerId;
        PlayerName = playerName;
        BasePrice = basePrice;
    }

    public Guid PlayerId { get; }

    public string PlayerName { get; }

    public decimal BasePrice { get; }
}

public class BidPlacedEvent : AuctionEvent
{
    public BidPlacedEvent(Guid teamId, decimal amount, DateTimeOffset occurredAt)
        : base(occurredAt)
    {
        TeamId = teamId;
        Amount = amount;
    }

    public Guid TeamId { get; }

    public decimal Amount { get; }
}

public class PlayerSoldEvent : AuctionEvent
{
    public PlayerSoldEvent(Guid playerId, Guid teamId, decimal soldPrice, DateTimeOffset occurredAt)
        : base(occurredAt)
    {
        PlayerId = playerId;
        TeamId = teamId;
        SoldPrice = soldPrice;
    }

    public Guid PlayerId { get; }

    public Guid TeamId { get; }

    public decimal SoldPrice { get; }
}

public class PlayerUnsoldEvent : AuctionEvent
{
    public PlayerUnsoldEvent(Guid playerId, DateTimeOffset occurredAt)
        : base(occurredAt)
    {
        PlayerId = playerId;
    }

    public Guid PlayerId { get; }
}

public class AuctionFinishedEvent : AuctionEvent
{
    public AuctionFinishedEvent(DateTimeOffset occurredAt)
        : base(occurredAt)
    {
    }
}
