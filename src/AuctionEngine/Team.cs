namespace AuctionEngine;

public class Team
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal PurseRemaining { get; set; }

    public List<Player> Squad { get; set; } = [];
}
