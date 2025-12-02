using CrazyBikeShop.Shared;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace CrazyBikeShop.Assembler;

//[DurableTask]
public class AssembleBikeActivity(ILogger<AssembleBikeActivity> logger) : TaskActivity<Bike, AssembledBike>
{
    public override async Task<AssembledBike> RunAsync(TaskActivityContext context, Bike bike)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5));

            var assembledBike = new AssembledBike
            {
                Id = bike.Id,
                Model = bike.Model,
                Message = $"Bike {bike.Model} with ID {bike.Id} assembled with {bike.Parts.Count} parts."
            };
        
            logger.LogInformation("AssembleBikeActivity called with response: {Message}", assembledBike.Message);
            return assembledBike;
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occurred in AssembleBikeActivity for Bike ID: {BikeId}", bike.Id);
            throw;
        }
    }
}