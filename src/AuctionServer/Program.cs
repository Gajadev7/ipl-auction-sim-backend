using AuctionEngine;
using AuctionServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<AuctionManager>();
builder.Services.AddSingleton<AuctionEventStream>();
builder.Services.AddSingleton<AiAuctionCoordinator>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<AiAuctionCoordinator>());

var app = builder.Build();

app.UseSwagger(swaggerOptions =>
{
    swaggerOptions.RouteTemplate = "swagger/{documentName}/swagger.json";
});
app.UseSwaggerUI(swaggerUiOptions =>
{
    swaggerUiOptions.RoutePrefix = "swagger";
    swaggerUiOptions.SwaggerEndpoint("/swagger/v1/swagger.json", "AuctionServer API v1");
});

app.MapGet("/swagger", () => Results.Redirect("/swagger/index.html"));

app.MapControllers();

app.Run();
