using Microsoft.DurableTask.Worker;
using Microsoft.DurableTask.Worker.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Configure the host builder
var builder = Host.CreateApplicationBuilder();
// Configure logging
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);
// Build a logger for startup configuration
using var loggerFactory = LoggerFactory.Create(loggingBuilder =>
{
    loggingBuilder.AddConsole();
    loggingBuilder.SetMinimumLevel(LogLevel.Information);
});
var logger = loggerFactory.CreateLogger<Program>();

var connectionString = CreateTaskHubConnection();

builder.Services.AddDurableTaskWorker()
    .AddTasks(registry =>
    {
        registry.AddActivity<CrazyBikeShop.Ship.ShipBikeActivity>();
    })
    .UseDurableTaskScheduler(connectionString, options =>
    {
        // Configure any options if needed
    });

var host = builder.Build();

// Get the logger from the service provider for the rest of the program
logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting ShipBikeActivity");
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

#region Local functions

string CreateTaskHubConnection()
{
    // Get environment variables for endpoint and taskhub with defaults
    var taskHubEndpoint = Environment.GetEnvironmentVariable("TASK_HUB_ENDPOINT") ?? "http://localhost:8080";
    var taskHubName = Environment.GetEnvironmentVariable("TASK_HUB_NAME") ?? "default";

    // Split the endpoint if it contains authentication info
    var hostAddress = taskHubEndpoint;
    if (taskHubEndpoint.Contains(';'))
        hostAddress = taskHubEndpoint.Split(';')[0];

    // Determine if we're connecting to the local emulator
    var isLocalEmulator = taskHubEndpoint == "http://localhost:8080";

    // Construct a proper connection string with authentication
    string s;
    if (isLocalEmulator)
    {
        // For local emulator, no authentication needed
        s = $"Endpoint={hostAddress};TaskHub={taskHubName};Authentication=None";
        logger.LogInformation("Using local emulator with no authentication");
    }
    else
    {
        // For Azure, use DefaultAzure - make sure TaskHub is included
        // Append the TaskHub parameter if it's not already in the connection string
        s = !taskHubEndpoint.Contains("TaskHub=") ? $"{taskHubEndpoint};TaskHub={taskHubName}" : taskHubEndpoint;
        logger.LogInformation("Using Azure endpoint with DefaultAzure");
    }

    logger.LogInformation("Using endpoint: {TaskHubEndpoint}", taskHubEndpoint);
    logger.LogInformation("Using task hub: {TaskHubName}", taskHubName);
    logger.LogInformation("Host address: {HostAddress}", hostAddress);
    logger.LogInformation("Connection string: {ConnectionString}", s);
    return s;
}

#endregion