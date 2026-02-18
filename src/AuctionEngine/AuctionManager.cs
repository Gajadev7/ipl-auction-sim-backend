namespace AuctionEngine;

public class AuctionManager
{
    private readonly List<Team> _teams = [];
    private readonly Dictionary<Guid, Team> _teamsById = [];
    private readonly Queue<Player> _playerQueue = [];
    private readonly HashSet<Guid> _passedTeamIds = [];

    private Player? _currentPlayer;
    private decimal? _currentHighestBid;
    private Guid? _currentHighestBidderTeamId;
    private bool _currentPlayerFinalized;

    private AuctionState _auctionState = AuctionState.NotStarted;

    public event Action<AuctionEvent> OnAuctionEvent = delegate { };

    public void StartAuction(List<Team> teams, List<Player> players)
    {
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(players);

        if (teams.Count == 0)
        {
            throw new ArgumentException("At least one team is required to start the auction.", nameof(teams));
        }

        _teams.Clear();
        _teamsById.Clear();
        _playerQueue.Clear();
        _passedTeamIds.Clear();
        _currentPlayer = null;
        _currentHighestBid = null;
        _currentHighestBidderTeamId = null;
        _currentPlayerFinalized = false;

        foreach (var team in teams)
        {
            var teamClone = CloneTeam(team);
            if (!_teamsById.TryAdd(teamClone.Id, teamClone))
            {
                throw new ArgumentException($"Duplicate team id detected: {teamClone.Id}.", nameof(teams));
            }

            _teams.Add(teamClone);
        }

        var playerIds = new HashSet<Guid>();
        foreach (var player in players)
        {
            var playerClone = ClonePlayer(player);
            if (!playerIds.Add(playerClone.Id))
            {
                throw new ArgumentException($"Duplicate player id detected: {playerClone.Id}.", nameof(players));
            }

            _playerQueue.Enqueue(playerClone);
        }

        var now = DateTimeOffset.UtcNow;
        TransitionAuctionState(
            AuctionState.PlayerNomination,
            new AuctionStartedEvent(_teams.Count, _playerQueue.Count, now));

        if (_playerQueue.Count == 0)
        {
            TransitionAuctionState(
                AuctionState.Finished,
                new AuctionFinishedEvent(DateTimeOffset.UtcNow));
        }
    }

    public void NominateNextPlayer()
    {
        if (_auctionState == AuctionState.NotStarted)
        {
            throw new InvalidOperationException("Auction has not been started.");
        }

        if (_auctionState == AuctionState.Bidding)
        {
            throw new InvalidOperationException("Cannot nominate next player while bidding is active.");
        }

        if (_auctionState == AuctionState.Finished)
        {
            throw new InvalidOperationException("Auction is already finished.");
        }

        if ((_auctionState == AuctionState.Sold || _auctionState == AuctionState.Unsold) && !_currentPlayerFinalized)
        {
            FinalizeSale();
        }

        _passedTeamIds.Clear();
        _currentHighestBid = null;
        _currentHighestBidderTeamId = null;
        _currentPlayerFinalized = false;

        if (_playerQueue.Count == 0)
        {
            _currentPlayer = null;
            TransitionAuctionState(
                AuctionState.Finished,
                new AuctionFinishedEvent(DateTimeOffset.UtcNow));
            return;
        }

        _currentPlayer = _playerQueue.Dequeue();
        TransitionAuctionState(
            AuctionState.Bidding,
            new PlayerNominatedEvent(
                _currentPlayer.Id,
                _currentPlayer.Name,
                _currentPlayer.BasePrice,
                DateTimeOffset.UtcNow));
    }

    public void PlaceBid(Guid teamId, decimal amount)
    {
        EnsureBiddingActive();

        var team = GetTeamOrThrow(teamId);

        if (_passedTeamIds.Contains(teamId))
        {
            throw new InvalidOperationException("Team has already passed for the current player.");
        }

        if (amount <= 0)
        {
            throw new InvalidOperationException("Bid amount must be greater than zero.");
        }

        if (_currentHighestBid.HasValue && amount <= _currentHighestBid.Value)
        {
            throw new InvalidOperationException("Bid must be strictly higher than the current highest bid.");
        }

        if (team.PurseRemaining < amount)
        {
            throw new InvalidOperationException("Team does not have enough purse remaining for this bid.");
        }

        _currentHighestBid = amount;
        _currentHighestBidderTeamId = teamId;
        PublishEvent(new BidPlacedEvent(teamId, amount, DateTimeOffset.UtcNow));

        if (IsOnlyHighestBidderRemaining())
        {
            TransitionAuctionState(AuctionState.Sold, CreatePlayerSoldEvent());
        }
    }

    public void Pass(Guid teamId)
    {
        EnsureBiddingActive();
        _ = GetTeamOrThrow(teamId);

        if (_currentHighestBidderTeamId == teamId)
        {
            throw new InvalidOperationException("Current highest bidder cannot pass.");
        }

        if (!_passedTeamIds.Add(teamId))
        {
            return;
        }

        if (_currentHighestBidderTeamId.HasValue && IsOnlyHighestBidderRemaining())
        {
            TransitionAuctionState(AuctionState.Sold, CreatePlayerSoldEvent());
            return;
        }

        if (!_currentHighestBidderTeamId.HasValue && _passedTeamIds.Count == _teams.Count)
        {
            TransitionAuctionState(AuctionState.Unsold, CreatePlayerUnsoldEvent());
        }
    }

    public void FinalizeSale()
    {
        if (_auctionState == AuctionState.NotStarted)
        {
            throw new InvalidOperationException("Auction has not been started.");
        }

        if (_currentPlayer is null)
        {
            throw new InvalidOperationException("No active player to finalize.");
        }

        if (_currentPlayerFinalized)
        {
            return;
        }

        if (_auctionState == AuctionState.Bidding)
        {
            if (_currentHighestBidderTeamId.HasValue)
            {
                TransitionAuctionState(AuctionState.Sold, CreatePlayerSoldEvent());
            }
            else
            {
                TransitionAuctionState(AuctionState.Unsold, CreatePlayerUnsoldEvent());
            }
        }

        if (_auctionState == AuctionState.Sold)
        {
            ApplySoldResult();
            _currentPlayerFinalized = true;
            return;
        }

        if (_auctionState == AuctionState.Unsold)
        {
            _currentPlayer.SoldPrice = null;
            _currentPlayer.SoldToTeamId = null;
            _currentPlayerFinalized = true;
            return;
        }

        throw new InvalidOperationException("Current auction state does not allow sale finalization.");
    }

    public AuctionManagerState GetCurrentState()
    {
        return new AuctionManagerState
        {
            AuctionState = _auctionState,
            Teams = _teams.Select(CloneTeam).ToList(),
            RemainingPlayers = _playerQueue.Select(ClonePlayer).ToList(),
            CurrentPlayer = _currentPlayer is null ? null : ClonePlayer(_currentPlayer),
            CurrentHighestBid = _currentHighestBid,
            CurrentHighestBidderTeamId = _currentHighestBidderTeamId
        };
    }

    private void ApplySoldResult()
    {
        if (!_currentHighestBidderTeamId.HasValue || !_currentHighestBid.HasValue)
        {
            throw new InvalidOperationException("Cannot finalize sold player without a highest bid and bidder.");
        }

        var winningTeamId = _currentHighestBidderTeamId.Value;
        var winningTeam = GetTeamOrThrow(winningTeamId);
        var soldPrice = _currentHighestBid.Value;

        if (winningTeam.PurseRemaining < soldPrice)
        {
            throw new InvalidOperationException("Winning team does not have enough purse remaining.");
        }

        winningTeam.PurseRemaining -= soldPrice;
        _currentPlayer!.SoldPrice = soldPrice;
        _currentPlayer.SoldToTeamId = winningTeamId;
        winningTeam.Squad.Add(_currentPlayer);
    }

    private Team GetTeamOrThrow(Guid teamId)
    {
        if (_teamsById.TryGetValue(teamId, out var team))
        {
            return team;
        }

        throw new InvalidOperationException($"Unknown team id: {teamId}.");
    }

    private void EnsureBiddingActive()
    {
        if (_auctionState != AuctionState.Bidding)
        {
            throw new InvalidOperationException("Bidding is not active.");
        }

        if (_currentPlayer is null)
        {
            throw new InvalidOperationException("No current player is nominated.");
        }
    }

    private bool IsOnlyHighestBidderRemaining()
    {
        if (!_currentHighestBidderTeamId.HasValue)
        {
            return false;
        }

        var highestBidderTeamId = _currentHighestBidderTeamId.Value;
        return _passedTeamIds.Count == _teams.Count - 1 && !_passedTeamIds.Contains(highestBidderTeamId);
    }

    private void TransitionAuctionState(AuctionState newState, AuctionEvent auctionEvent)
    {
        if (_auctionState == newState)
        {
            return;
        }

        _auctionState = newState;
        PublishEvent(auctionEvent);
    }

    private void PublishEvent(AuctionEvent auctionEvent)
    {
        OnAuctionEvent.Invoke(auctionEvent);
    }

    private PlayerSoldEvent CreatePlayerSoldEvent()
    {
        if (_currentPlayer is null || !_currentHighestBidderTeamId.HasValue || !_currentHighestBid.HasValue)
        {
            throw new InvalidOperationException("Cannot create a sold event without player, bidder, and bid.");
        }

        return new PlayerSoldEvent(
            _currentPlayer.Id,
            _currentHighestBidderTeamId.Value,
            _currentHighestBid.Value,
            DateTimeOffset.UtcNow);
    }

    private PlayerUnsoldEvent CreatePlayerUnsoldEvent()
    {
        if (_currentPlayer is null)
        {
            throw new InvalidOperationException("Cannot create an unsold event without a current player.");
        }

        return new PlayerUnsoldEvent(_currentPlayer.Id, DateTimeOffset.UtcNow);
    }

    private static Team CloneTeam(Team team)
    {
        return new Team
        {
            Id = team.Id,
            Name = team.Name,
            PurseRemaining = team.PurseRemaining,
            Squad = team.Squad.Select(ClonePlayer).ToList()
        };
    }

    private static Player ClonePlayer(Player player)
    {
        return new Player
        {
            Id = player.Id,
            Name = player.Name,
            Role = player.Role,
            BasePrice = player.BasePrice,
            SoldPrice = player.SoldPrice,
            SoldToTeamId = player.SoldToTeamId
        };
    }
}
