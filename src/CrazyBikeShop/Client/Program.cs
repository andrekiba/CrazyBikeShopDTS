using Microsoft.DurableTask.Client;
using Microsoft.DurableTask.Client.AzureManaged;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using CrazyBikeShop.Shared;
using Microsoft.DurableTask;

var logger = CreateLogger();

var connectionString = CreateTaskHubConnection();

// Create the client using DI service provider
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());

// Register the durable task client
services.AddDurableTaskClient(options =>
{
    options.UseDurableTaskScheduler(connectionString);
});
var serviceProvider = services.BuildServiceProvider();
var client = serviceProvider.GetRequiredService<DurableTaskClient>();

// Create input to start n bike order orchestrations
logger.LogInformation("Starting sequential orchestration scheduler - 10 orchestrations, 1 every 5 seconds");

// Set up orchestration parameters
const int totalOrchestrations = 10;  // Total number of orchestrations to run
const int intervalSeconds = 5;       // Time between orchestrations in seconds
var completedOrchestrations = 0;     // Track total completed orchestrations
var failedOrchestrations = 0;        // Track total failed orchestrations

var orchestrations = Enumerable.Range(1, totalOrchestrations).Select(_ => new StartBikeOrderOrchestrator
{
    InstanceId = Guid.NewGuid().ToString(),
    Bike = CrazyBikeSelector.GetOne()
}).ToList();

// Run the main workflow to schedule and wait for all orchestrations
await RunSequentialOrchestrations();

logger.LogInformation("All orchestrations completed. Application shutting down.");
return;

#region Local functions

ILogger<Program> CreateLogger()
{
    using var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    });

    var logger1 = loggerFactory.CreateLogger<Program>();
    logger1.LogInformation("Starting Crazy Bike Orchestration Client");
    return logger1;
}

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

async Task RunSequentialOrchestrations()
{
    // List to track all instance ids for monitoring
    var allInstanceIds = orchestrations.Select(o => o.InstanceId).ToList();
    
    // Schedule each orchestration with delay between them
    foreach (var (o, i) in orchestrations.Select((orchestrator, index) => (orchestrator, index)))
    {
        // Create a unique instance ID
        logger.LogInformation("Scheduling orchestration #{Number} ({InstanceName})", i+1, o.InstanceId);
        
        try
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Schedule the orchestration
            await client.ScheduleNewOrchestrationInstanceAsync(
                "CrazyBikeOrchestration", 
                o.Bike, new StartOrchestrationOptions{ InstanceId = o.InstanceId });
            
            stopwatch.Stop();
            
            logger.LogInformation("Orchestration #{Number} scheduled in {ElapsedMs}ms with ID: {InstanceId}", 
                i+1, stopwatch.ElapsedMilliseconds, o.InstanceId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error scheduling orchestration #{Number}", i+1);
        }
        
        // Wait before scheduling next orchestration (except for the last one)
        if (i < totalOrchestrations - 1)
        {
            logger.LogInformation("Waiting {Seconds} seconds before scheduling next orchestration...", intervalSeconds);
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
        }
    }
    
    logger.LogInformation("All {Count} orchestrations scheduled. Waiting for completion...", allInstanceIds.Count);

    // Now wait for all orchestrations to complete
    foreach (var id in allInstanceIds)
    {
        try
        {
            var instance = await client.WaitForInstanceCompletionAsync(
                id, getInputsAndOutputs: false, CancellationToken.None);

            switch (instance.RuntimeStatus)
            {
                case OrchestrationRuntimeStatus.Completed:
                    completedOrchestrations++;
                    logger.LogInformation("Orchestration {Id} completed successfully", instance.InstanceId);
                    break;
                case OrchestrationRuntimeStatus.Failed:
                    failedOrchestrations++;
                    logger.LogError("Orchestration {Id} failed: {ErrorMessage}", 
                        instance.InstanceId, instance.FailureDetails?.ErrorMessage);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error waiting for orchestration {Id} completion", id);
        }
    }
    
    // Log final stats
    logger.LogInformation("FINAL RESULTS: {Completed} completed, {Failed} failed, {Total} total orchestrations", 
        completedOrchestrations, failedOrchestrations, allInstanceIds.Count);
}

#endregion


