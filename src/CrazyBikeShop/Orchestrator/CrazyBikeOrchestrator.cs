using CrazyBikeShop.Shared;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace CrazyBikeShop.Orchestrator;

[DurableTask]
public class CrazyBikeOrchestrator : TaskOrchestrator<Bike, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, Bike bike)
    {
        // Step 1: Assemble the bike
        var assembledBike = await context.CallActivityAsync<string>("AssembleBikeActivity", bike);
        
        // Step 2: Ship the bike
        var shipConfirmation = await context.CallActivityAsync<string>("ShipBikeActivity", assembledBike);
        
        // Step 3: Finalize the response
        var finalResponse = await context.CallActivityAsync<string>(nameof(FinalizeResponseActivity), shipConfirmation);
        
        return finalResponse;
    }
}

[DurableTask]
public class FinalizeResponseActivity(ILogger<FinalizeResponseActivity> logger) : TaskActivity<string, string>
{
    public override Task<string> RunAsync(TaskActivityContext context, string shipConfirmation)
    {
        logger.LogInformation("Activity FinalizeResponse called with response: {Response}", shipConfirmation);
        
        // Third activity that finalizes the response
        return Task.FromResult(shipConfirmation);
    }
}