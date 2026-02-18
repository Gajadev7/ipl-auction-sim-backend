using System.Threading.Channels;
using AuctionEngine;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuctionServer.Services;

public sealed class AiAuctionCoordinator : IHostedService, IDisposable
{
    private readonly AuctionManager _auctionManager;
    private readonly BidderRegistry _bidderRegistry;
    private readonly ILogger<AiAuctionCoordinator> _logger;
    private readonly Channel<AuctionEvent> _eventChannel;
    private readonly Dictionary<Guid, AiBidder> _aiBidders = [];
    private readonly HashSet<Guid> _aiTeamsThatPassed = [];
    private readonly Random _random = new();
    private readonly SemaphoreSlim _roundGate = new(1, 1);

    private CancellationTokenSource? _shutdownCts;
    private Task? _eventWorkerTask;
    private bool _disposed;

    public AiAuctionCoordinator(
        AuctionManager auctionManager,
        BidderRegistry bidderRegistry,
        ILogger<AiAuctionCoordinator> logger)
    {
        _auctionManager = auctionManager;
        _bidderRegistry = bidderRegistry;
        _logger = logger;
        _eventChannel = Channel.CreateUnbounded<AuctionEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _auctionManager.OnAuctionEvent += HandleAuctionEvent;
        _eventWorkerTask = Task.Run(() => ProcessEventsAsync(_shutdownCts.Token), _shutdownCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _auctionManager.OnAuctionEvent -= HandleAuctionEvent;
        _eventChannel.Writer.TryComplete();

        if (_shutdownCts is not null)
        {
            _shutdownCts.Cancel();
        }

        if (_eventWorkerTask is null)
        {
            return;
        }

        try
        {
            await _eventWorkerTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "AI event worker stopped with an exception.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdownCts?.Dispose();
        _roundGate.Dispose();
    }

    private void HandleAuctionEvent(AuctionEvent auctionEvent)
    {
        _eventChannel.Writer.TryWrite(auctionEvent);
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        await foreach (var auctionEvent in _eventChannel.Reader.ReadAllAsync(cancellationToken))
        {
            foreach (var bidder in _aiBidders.Values)
            {
                bidder.Observe(auctionEvent);
            }

            switch (auctionEvent)
            {
                case AuctionStartedEvent:
                    InitializeBidders();
                    break;
                case PlayerNominatedEvent:
                    _aiTeamsThatPassed.Clear();
                    await RunAiRoundAsync(cancellationToken);
                    break;
                case PlayerSoldEvent:
                case PlayerUnsoldEvent:
                    _aiTeamsThatPassed.Clear();
                    break;
            }
        }
    }

    private void InitializeBidders()
    {
        _aiBidders.Clear();
        _aiTeamsThatPassed.Clear();

        var state = _auctionManager.GetCurrentState();
        var registeredAiTeamIds = _bidderRegistry.GetAiTeamIds();
        var aiTeams = state.Teams.Where(team => registeredAiTeamIds.Contains(team.Id)).ToList();
        if (aiTeams.Count == 0)
        {
            aiTeams = state.Teams.ToList();
        }

        foreach (var team in aiTeams)
        {
            var seed = HashCode.Combine(team.Id, _random.Next());
            _aiBidders[team.Id] = new AiBidder(team.Id, team.Name, seed);
        }

        _logger.LogInformation("Initialized {AiTeamCount} AI bidders.", _aiBidders.Count);
    }

    private async Task RunAiRoundAsync(CancellationToken cancellationToken)
    {
        if (!await _roundGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            const int maxIterations = 200;
            for (var i = 0; i < maxIterations; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                var state = _auctionManager.GetCurrentState();
                if (state.AuctionState != AuctionState.Bidding || state.CurrentPlayer is null)
                {
                    return;
                }

                var aiTeamsInRound = state.Teams
                    .Where(team => _aiBidders.ContainsKey(team.Id))
                    .OrderBy(_ => _random.Next())
                    .ToList();

                if (aiTeamsInRound.Count == 0)
                {
                    return;
                }

                var actedThisIteration = false;

                foreach (var team in aiTeamsInRound)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var currentState = _auctionManager.GetCurrentState();
                    if (currentState.AuctionState != AuctionState.Bidding || currentState.CurrentPlayer is null)
                    {
                        return;
                    }

                    if (!_aiBidders.TryGetValue(team.Id, out var bidder))
                    {
                        continue;
                    }

                    if (_aiTeamsThatPassed.Contains(team.Id))
                    {
                        continue;
                    }

                    var teamState = currentState.Teams.FirstOrDefault(value => value.Id == team.Id);
                    if (teamState is null)
                    {
                        continue;
                    }

                    var decision = bidder.Decide(teamState, currentState);

                    try
                    {
                        switch (decision.DecisionType)
                        {
                            case AiBidDecisionType.Bid:
                                _auctionManager.PlaceBid(team.Id, decision.Amount);
                                actedThisIteration = true;
                                break;
                            case AiBidDecisionType.Pass:
                                _auctionManager.Pass(team.Id);
                                _aiTeamsThatPassed.Add(team.Id);
                                actedThisIteration = true;
                                break;
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // State can change between decision and action due to other participants.
                    }
                    catch (ArgumentException)
                    {
                        // Defensive guard: invalid operation inputs are ignored for AI flow.
                    }

                    await Task.Delay(_random.Next(120, 300), cancellationToken);
                }

                if (!actedThisIteration)
                {
                    return;
                }
            }

            _logger.LogWarning("AI round reached max iterations and was stopped to avoid an infinite loop.");
        }
        finally
        {
            _roundGate.Release();
        }
    }

}
