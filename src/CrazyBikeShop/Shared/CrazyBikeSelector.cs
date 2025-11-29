using Bogus;

namespace CrazyBikeShop.Shared;

public static class CrazyBikeSelector
{
    static readonly string[] BikePartNames =
    [
        "wheel", "rim", "tire", "brake", "seat", "cassette", "rear-derailleur", "front-derailleur",  
        "chain", "chainring", "crankset", "pedal", "headset", "stem", "handlerbar", "fork", "frame",
        "hub", "bottle-cage", "disk"
    ];
    static readonly string[] BikeModels =
    [
        "mtb-xc", "mtb-trail", "mtb-enduro", "mtb-downhill", "bdc-aero",
        "bdc-endurance", "gravel", "ciclocross", "trekking", "urban"
    ];
    
    public static Bike GetOne(string? model = null)
    {
        var bikePartGen = new Faker<BikePart>()
            .RuleFor(x => x.Id, () => Guid.NewGuid().ToString())
            .RuleFor(x => x.Name, f => f.PickRandom(BikePartNames))
            .RuleFor(x => x.Code, f => f.Commerce.Ean8());

        var bikeGen = new Faker<Bike>()
            .RuleFor(x => x.Id, () => Guid.NewGuid().ToString())
            .RuleFor(x => x.Price, f => f.Random.Number(200,10000))
            .RuleFor(x => x.Model, f => string.IsNullOrEmpty(model) ? f.PickRandom(BikeModels) : model)
            .RuleFor(u => u.Parts, f => bikePartGen.Generate(f.Random.Number(6,BikePartNames.Length)));
            
        return bikeGen.Generate();
    }
}