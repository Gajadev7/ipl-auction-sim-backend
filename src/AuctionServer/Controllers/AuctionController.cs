using AuctionEngine;
using AuctionServer.Contracts;
using AuctionServer.Models;
using AuctionServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace AuctionServer.Controllers;

[ApiController]
[Route("auction")]
public class AuctionController : ControllerBase
{
    private const int TeamCount = 10;
    private const decimal TeamInitialPurse = 100m;

    private readonly AuctionManager _auctionManager;
    private readonly AuctionEventStream _auctionEventStream;
    private readonly PlayerLoader _playerLoader;
    private readonly BidderRegistry _bidderRegistry;

    public AuctionController(
        AuctionManager auctionManager,
        AuctionEventStream auctionEventStream,
        PlayerLoader playerLoader,
        BidderRegistry bidderRegistry)
    {
        _auctionManager = auctionManager;
        _auctionEventStream = auctionEventStream;
        _playerLoader = playerLoader;
        _bidderRegistry = bidderRegistry;
    }

    [HttpPost("start")]
    public ActionResult<AuctionManagerState> Start()
    {
        return Execute(() =>
        {
            var playerDtos = _playerLoader.LoadPlayers();
            var players = playerDtos.Select(MapPlayer).ToList();
            var teams = CreateTeamsAndRegisterBidders();

            _auctionManager.StartAuction(teams, players);
        });
    }

    [HttpPost("bid")]
    public ActionResult<AuctionManagerState> Bid([FromBody] PlaceBidRequest request)
    {
        return Execute(() => _auctionManager.PlaceBid(request.TeamId, request.Amount));
    }

    [HttpPost("pass")]
    public ActionResult<AuctionManagerState> Pass([FromBody] PassRequest request)
    {
        return Execute(() => _auctionManager.Pass(request.TeamId));
    }

    [HttpPost("next")]
    public ActionResult<AuctionManagerState> Next()
    {
        return Execute(_auctionManager.NominateNextPlayer);
    }

    [HttpGet("state")]
    public ActionResult<AuctionManagerState> GetState()
    {
        return Ok(_auctionManager.GetCurrentState());
    }

    [HttpGet("events")]
    public async Task Events(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.Headers.Append("X-Accel-Buffering", "no");
        Response.ContentType = "text/event-stream";

        await Response.StartAsync(cancellationToken);
        await Response.WriteAsync(": connected\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);

        await using var subscription = _auctionEventStream.Subscribe();
        using var keepAliveTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var readEventTask = subscription.Reader.ReadAsync(cancellationToken).AsTask();
                var keepAliveTask = keepAliveTimer.WaitForNextTickAsync(cancellationToken).AsTask();
                var completedTask = await Task.WhenAny(readEventTask, keepAliveTask);

                if (completedTask == readEventTask)
                {
                    var eventMessage = await readEventTask;
                    await Response.WriteAsync(eventMessage, cancellationToken);
                }
                else
                {
                    if (!await keepAliveTask)
                    {
                        break;
                    }

                    await Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                }

                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private ActionResult<AuctionManagerState> Execute(Action operation)
    {
        try
        {
            operation();
            return Ok(_auctionManager.GetCurrentState());
        }
        catch (ArgumentException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }

    private List<Team> CreateTeamsAndRegisterBidders()
    {
        _bidderRegistry.Clear();

        var teams = new List<Team>(TeamCount);
        for (var teamNumber = 1; teamNumber <= TeamCount; teamNumber++)
        {
            var team = new Team
            {
                Id = CreateDeterministicGuid(teamNumber),
                Name = $"Team {teamNumber}",
                PurseRemaining = TeamInitialPurse,
                Squad = []
            };

            if (teamNumber == 1)
            {
                _bidderRegistry.RegisterHuman(team.Id);
            }
            else
            {
                _bidderRegistry.RegisterAi(team.Id);
            }

            teams.Add(team);
        }

        return teams;
    }

    private static Player MapPlayer(PlayerDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new ArgumentException($"Player name is required for player id {dto.Id}.");
        }

        if (dto.BasePrice <= 0m)
        {
            throw new ArgumentException($"BasePrice must be greater than zero for player id {dto.Id}.");
        }

        return new Player
        {
            Id = CreateDeterministicGuid(dto.Id),
            Name = dto.Name.Trim(),
            Role = ParseRole(dto.Role),
            BasePrice = dto.BasePrice
        };
    }

    private static PlayerRole ParseRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("Player role is required.");
        }

        var normalizedRole = role.Trim().Replace("-", string.Empty).Replace(" ", string.Empty);

        return normalizedRole.ToLowerInvariant() switch
        {
            "batsman" => PlayerRole.Batsman,
            "bowler" => PlayerRole.Bowler,
            "allrounder" => PlayerRole.AllRounder,
            "wicketkeeper" => PlayerRole.WicketKeeper,
            _ => throw new ArgumentException($"Unsupported player role '{role}'.")
        };
    }

    private static Guid CreateDeterministicGuid(int value)
    {
        return new Guid(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }
}
