using CrazyBikeShop.Shared;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace CrazyBikeShop.Orchestrator;

//[DurableTask]
public class CrazyBikeOrchestrator : TaskOrchestrator<Bike, string>
{
    public override async Task<string> RunAsync(TaskOrchestrationContext context, Bike bike)
    {
        var logger = context.CreateReplaySafeLogger(nameof(CrazyBikeOrchestrator));
        
        try
        {
            // Step 1: Assemble the bike
            logger.LogInformation("Starting bike assembly for Bike ID: {BikeId}", bike.Id);
            var assembledBike = await context.CallActivityAsync<AssembledBike>(nameof(AssembleBikeActivity), bike);
            
            // Step 2: Ship the bike
            logger.LogInformation("Starting bike shipping for Bike ID: {BikeId}", bike.Id);
            var shippedBike = await context.CallActivityAsync<ShippedBike>(nameof(ShipBikeActivity), assembledBike);
            
            return shippedBike.Message;
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred in CrazyBikeOrchestrator for Bike ID: {BikeId}", bike.Id);
            throw;
        }
    }
}