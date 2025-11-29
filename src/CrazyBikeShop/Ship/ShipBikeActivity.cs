using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;

namespace CrazyBikeShop.Ship;

[DurableTask]
public class ShipBikeActivity : TaskActivity<string, string>
{
    readonly ILogger<ShipBikeActivity> logger;

    public ShipBikeActivity(ILogger<ShipBikeActivity> logger)
    {
        this.logger = logger;
    }

    public override async Task<string> RunAsync(TaskActivityContext context, string assembledBike)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        
        var shipConfirmation = $"Bike {assembledBike} shipped!";
        
        logger.LogInformation("ShipBikeActivity called with response: {ShipConfirmation}", shipConfirmation);
        
        return assembledBike;
    }
}