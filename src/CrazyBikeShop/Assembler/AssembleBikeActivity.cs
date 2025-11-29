using CrazyBikeShop.Shared;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace CrazyBikeShop.Assembler;

[DurableTask]
public class AssembleBikeActivity(ILogger<AssembleBikeActivity> logger) : TaskActivity<Bike, string>
{
    public override async Task<string> RunAsync(TaskActivityContext context, Bike bike)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        
        var assembledBike = $"Bike {bike.Model} with ID {bike.Id} assembled with {bike.Parts.Count} parts.";
        
        logger.LogInformation("AssembleBikeActivity called with response: {AssembledBike}", assembledBike);
        
        return assembledBike;
    }
}