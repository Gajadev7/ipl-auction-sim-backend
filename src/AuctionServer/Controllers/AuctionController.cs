using AuctionEngine;
using AuctionServer.Contracts;
using AuctionServer.Services;
using Microsoft.AspNetCore.Mvc;

namespace AuctionServer.Controllers;

[ApiController]
[Route("auction")]
public class AuctionController : ControllerBase
{
    private readonly AuctionManager _auctionManager;
    private readonly AuctionEventStream _auctionEventStream;

    public AuctionController(AuctionManager auctionManager, AuctionEventStream auctionEventStream)
    {
        _auctionManager = auctionManager;
        _auctionEventStream = auctionEventStream;
    }

    [HttpPost("start")]
    public ActionResult<AuctionManagerState> Start([FromBody] StartAuctionRequest request)
    {
        return Execute(() => _auctionManager.StartAuction(request.Teams, request.Players));
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
}
