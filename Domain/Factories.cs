namespace LogiCore.Domain;

public record VehicleSpec(string Type, string Number, double MaxLoadKg, double MaxVolumeM3,
    double SpeedKmH, decimal RatePerKm)
{
    public double MinTemperatureC { get; init; }

    public double MaxTemperatureC { get; init; }

    public double MaxRangeKm { get; init; }
}

public record CargoSpec(string Type, string Description, double WeightKg, double VolumeM3, decimal DeclaredValue)
{
    public DateTime ExpirationDate { get; init; }

    public double RequiredTemperatureC { get; init; }

    public double RiskFactor { get; init; } = 1;

    public int HazardClass { get; init; } = 1;

    public double LengthM { get; init; } = 1;
}

public class VehicleFactory
{
    private readonly Dictionary<string, Func<VehicleSpec, Vehicle>> _creators = new();

    public VehicleFactory()
    {
        Register("truck", s => new Truck(s.Number, s.MaxLoadKg, s.MaxVolumeM3, s.SpeedKmH, s.RatePerKm));
        Register("refrigerator", s => new RefrigeratorTruck(s.Number, s.MaxLoadKg, s.MaxVolumeM3, s.SpeedKmH,
            s.RatePerKm, s.MinTemperatureC, s.MaxTemperatureC));
        Register("plane", s => new CargoPlane(s.Number, s.MaxLoadKg, s.MaxVolumeM3, s.SpeedKmH, s.RatePerKm));
        Register("ship", s => new CargoShip(s.Number, s.MaxLoadKg, s.MaxVolumeM3, s.SpeedKmH, s.RatePerKm));
        Register("drone", s => new DroneCourier(s.Number, s.MaxLoadKg, s.MaxVolumeM3, s.SpeedKmH, s.RatePerKm,
            s.MaxRangeKm));
    }

    public IReadOnlyCollection<string> KnownTypes => _creators.Keys.ToList().AsReadOnly();

    public void Register(string type, Func<VehicleSpec, Vehicle> creator) => _creators[type] = creator;

    public Vehicle Create(VehicleSpec spec) => _creators.TryGetValue(spec.Type, out var creator)
        ? creator(spec)
        : throw new ArgumentException($"Неизвестный тип транспорта «{spec.Type}».");
}

public class CargoFactory
{
    private readonly Dictionary<string, Func<CargoSpec, Cargo>> _creators = new();

    public CargoFactory()
    {
        Register("standard", s => new StandardCargo(s.Description, s.WeightKg, s.VolumeM3, s.DeclaredValue));
        Register("perishable", s => new PerishableCargo(s.Description, s.WeightKg, s.VolumeM3, s.DeclaredValue,
            s.ExpirationDate, s.RequiredTemperatureC));
        Register("fragile", s => new FragileCargo(s.Description, s.WeightKg, s.VolumeM3, s.DeclaredValue, s.RiskFactor));
        Register("dangerous", s => new DangerousCargo(s.Description, s.WeightKg, s.VolumeM3, s.DeclaredValue,
            s.HazardClass));
        Register("oversized", s => new OversizedCargo(s.Description, s.WeightKg, s.VolumeM3, s.DeclaredValue, s.LengthM));
    }

    public IReadOnlyCollection<string> KnownTypes => _creators.Keys.ToList().AsReadOnly();

    public void Register(string type, Func<CargoSpec, Cargo> creator) => _creators[type] = creator;

    public Cargo Create(CargoSpec spec) => _creators.TryGetValue(spec.Type, out var creator)
        ? creator(spec)
        : throw new ArgumentException($"Неизвестный тип груза «{spec.Type}».");
}
