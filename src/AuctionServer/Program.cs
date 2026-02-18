using AuctionEngine;
using AuctionServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<AuctionManager>();
builder.Services.AddSingleton<AuctionEventStream>();
builder.Services.AddSingleton<AiAuctionCoordinator>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<AiAuctionCoordinator>());

var app = builder.Build();

app.MapControllers();

app.Run();
