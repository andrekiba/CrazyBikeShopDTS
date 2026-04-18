using CrazyBikeShop.Shared;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace CrazyBikeShop.Orchestrator;

/// <summary>
/// The orchestrator calls two activities sequentially:
///   1. AssembleBikeActivity  (handled by Assembler Worker)
///   2. ShipBikeActivity      (handled by Shipper Worker)
/// Because each activity is registered in a different worker process, DTS routes
/// each activity work item to the correct worker via work item filtering.
/// </summary>

//[DurableTask(nameof(CrazyBikeOrchestrator))]
public class CrazyBikeOrchestrator : TaskOrchestrator<Bike, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, Bike bike)
    {
        var logger = context.CreateReplaySafeLogger(nameof(CrazyBikeOrchestrator));
        
        try
        {
            // Step 1: Assemble the bike (routed to Assembler worker)
            logger.LogInformation("Starting bike assembly for Bike ID: {BikeId}", bike.Id);
            // var activityOptions = new TaskOptions
            // {
            //     Retry = new TaskRetryOptions(new RetryPolicy(2, TimeSpan.FromSeconds(1), 2))
            // };
            var assembledBike = await context.CallActivityAsync<AssembledBike>(Activities.AssembleBikeActivity, bike);
            
            // Step 2: Ship the bike (routed to Shipping worker)
            logger.LogInformation("Starting bike shipping for Bike ID: {BikeId}", bike.Id);
            var shippedBike = await context.CallActivityAsync<ShippedBike>(Activities.ShipBikeActivity, assembledBike);
            
            return shippedBike.Message;
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred in CrazyBikeOrchestrator for Bike ID: {BikeId}", bike.Id);
            throw;
        }
    }
}