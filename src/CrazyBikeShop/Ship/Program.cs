using CrazyBikeShop.ServiceDefaults;
using CrazyBikeShop.Ship;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Configure the host builder
var builder = Host.CreateApplicationBuilder();
builder.AddServiceDefaults();
// Configure logging
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss";
});
builder.Logging.SetMinimumLevel(LogLevel.Information);

builder.Services.AddDurableTaskWorker()
    .AddTasks(registry =>
    {
        registry.AddAllGeneratedTasks(); 
        //registry.AddActivity<ShipBikeActivity>();
    })
    .UseWorkItemFilters(new DurableTaskWorkerWorkItemFilters
    {
        Orchestrations = [],
        Activities = [new DurableTaskWorkerWorkItemFilters.ActivityFilter(nameof(ShipBikeActivity), null)]
    })
    .UseDurableTaskScheduler(Environment.GetEnvironmentVariable("ConnectionStrings__dts")!, options =>
    {
        // Configure any options if needed
    });

var host = builder.Build();

// Get the logger
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting Shipping worker");
await host.StartAsync();

logger.LogInformation("Worker started and waiting for tasks...");

// Wait indefinitely in environments without interactive console,
// or until a key is pressed in interactive environments
if (Environment.UserInteractive && !Console.IsInputRedirected)
{
    logger.LogInformation("Press any key to stop...");
    Console.ReadKey();
}
else
{
    // In non-interactive environments (like containers), wait indefinitely
    await Task.Delay(Timeout.InfiniteTimeSpan);
}

// Stop the host
await host.StopAsync();