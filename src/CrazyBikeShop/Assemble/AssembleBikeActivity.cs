using CrazyBikeShop.Shared;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace CrazyBikeShop.Assemble;

/// <summary>
/// Assemble the bike. This activity is registered only in Assembler worker,
/// so DTS will route AssembleBikeActivity work items exclusively to Assembler worker.
/// </summary>
[DurableTask(nameof(AssembleBikeActivity))]
public class AssembleBikeActivity(ILogger<AssembleBikeActivity> logger) : TaskActivity<Bike, AssembledBike>
{
    public override async Task<AssembledBike> RunAsync(TaskActivityContext context, Bike bike)
    {
        try
        {
            //delay between 5 and 30 seconds to simulate assembly time
            await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(5, 31)));

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