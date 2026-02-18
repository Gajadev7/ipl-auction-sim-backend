namespace AuctionEngine;

public class Player
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public PlayerRole Role { get; set; }

    public decimal BasePrice { get; set; }

    public decimal? SoldPrice { get; set; }

    public Guid? SoldToTeamId { get; set; }
}
