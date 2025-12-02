using CrazyBikeShop.Shared;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace CrazyBikeShop.Orchestrator;

//[DurableTask]
public class ShipBikeActivity(ILogger<ShipBikeActivity> logger) : TaskActivity<AssembledBike, ShippedBike>
{
    public override async Task<ShippedBike> RunAsync(TaskActivityContext context, AssembledBike assembledBike)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
        
            var shippedBike = new ShippedBike
            {
                Id = assembledBike.Id,
                Model =  assembledBike.Model,
                Message = $"Bike {assembledBike.Model} with ID {assembledBike.Id} shipped!"
            };
        
            logger.LogInformation("ShipBikeActivity called with response: {Message}", shippedBike.Message);
        
            return shippedBike;
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred in ShipBikeActivity for Bike ID: {BikeId}", assembledBike.Id);
            throw;
        }
    }
}