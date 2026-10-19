using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LogiCore.Domain;

public enum VehicleState
{
    Free,
    InTransit,
    UnderMaintenance
}

[Flags]
public enum TransportConditions
{
    None = 0,
    Refrigerated = 1,
    Sealed = 2,
    Pressurized = 4,
    LongRange = 8
}

public interface ITemperatureControlled
{
    bool SupportsTemperature(double temperatureC);
}

[JsonDerivedType(typeof(Truck), "truck")]
[JsonDerivedType(typeof(RefrigeratorTruck), "refrigerator")]
[JsonDerivedType(typeof(CargoPlane), "plane")]
[JsonDerivedType(typeof(CargoShip), "ship")]
[JsonDerivedType(typeof(DroneCourier), "drone")]
public abstract class Vehicle : IEntity
{
    private string _registrationNumber = string.Empty;

    protected Vehicle(string registrationNumber, double maxLoadKg, double maxVolumeM3,
        double averageSpeedKmH, decimal baseRatePerKm, Guid id = default)
    {
        if (maxLoadKg <= 0 || maxVolumeM3 <= 0 || averageSpeedKmH <= 0 || baseRatePerKm < 0)
        {
            throw new ArgumentException($"Некорректные характеристики ТС {registrationNumber}.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        RegistrationNumber = registrationNumber;
        MaxLoadKg = maxLoadKg;
        MaxVolumeM3 = maxVolumeM3;
        AverageSpeedKmH = averageSpeedKmH;
        BaseRatePerKm = baseRatePerKm;
    }

    public Guid Id { get; }

    public string RegistrationNumber
    {
        get => _registrationNumber;
        private set
        {
            if (value is null || !Regex.IsMatch(value, @"^[A-Z]{2}-\d{4}$"))
            {
                throw new ArgumentException($"Номер «{value}» не соответствует формату AA-0000.");
            }

            _registrationNumber = value;
        }
    }

    public double MaxLoadKg { get; }

    public double MaxVolumeM3 { get; }

    public double AverageSpeedKmH { get; }

    public decimal BaseRatePerKm { get; }

    [JsonInclude]
    public VehicleState State { get; private set; } = VehicleState.Free;

    [JsonIgnore]
    public abstract TransportConditions Conditions { get; }

    public abstract decimal CalculateDeliveryCost(Route route, IReadOnlyCollection<Cargo> cargo);

    public virtual bool CanCarry(Cargo cargo) => cargo.WeightKg <= MaxLoadKg && cargo.VolumeM3 <= MaxVolumeM3;

    public virtual bool CanServe(Route route) => true;

    public void SetState(VehicleState state) => State = state;

    public override bool Equals(object? obj) => obj is Vehicle other && other.Id == Id;

    public override int GetHashCode() => Id.GetHashCode();

    public override string ToString() =>
        $"{GetType().Name} {RegistrationNumber} ({MaxLoadKg} кг, {MaxVolumeM3} м³, {AverageSpeedKmH} км/ч, {State})";

    protected decimal DistanceCost(Route route) => (decimal)route.DistanceKm * BaseRatePerKm;
}

public class Truck : Vehicle
{
    public const decimal TollRoadFactor = 1.2m;

    public Truck(string registrationNumber, double maxLoadKg, double maxVolumeM3,
        double averageSpeedKmH, decimal baseRatePerKm, Guid id = default)
        : base(registrationNumber, maxLoadKg, maxVolumeM3, averageSpeedKmH, baseRatePerKm, id) { }

    public override TransportConditions Conditions => TransportConditions.Sealed | TransportConditions.LongRange;

    public override decimal CalculateDeliveryCost(Route route, IReadOnlyCollection<Cargo> cargo) =>
        DistanceCost(route) * TollRoadFactor;
}

public class RefrigeratorTruck : Truck, ITemperatureControlled
{
    public const decimal RefrigerationFactor = 1.25m;

    public RefrigeratorTruck(string registrationNumber, double maxLoadKg, double maxVolumeM3,
        double averageSpeedKmH, decimal baseRatePerKm, double minTemperatureC, double maxTemperatureC,
        Guid id = default)
        : base(registrationNumber, maxLoadKg, maxVolumeM3, averageSpeedKmH, baseRatePerKm, id)
    {
        MinTemperatureC = minTemperatureC;
        MaxTemperatureC = maxTemperatureC;
    }

    public double MinTemperatureC { get; }

    public double MaxTemperatureC { get; }

    public override TransportConditions Conditions => base.Conditions | TransportConditions.Refrigerated;

    public bool SupportsTemperature(double temperatureC) =>
        temperatureC >= MinTemperatureC && temperatureC <= MaxTemperatureC;

    public override bool CanCarry(Cargo cargo) => base.CanCarry(cargo)
        && (cargo is not ITemperatureSensitive sensitive || SupportsTemperature(sensitive.RequiredTemperatureC));

    public override decimal CalculateDeliveryCost(Route route, IReadOnlyCollection<Cargo> cargo) =>
        base.CalculateDeliveryCost(route, cargo) * RefrigerationFactor;

    public override string ToString() => $"{base.ToString()}, {MinTemperatureC}..{MaxTemperatureC} °C";
}

public class CargoPlane : Vehicle
{
    public const decimal PerKgSurcharge = 0.8m;

    public CargoPlane(string registrationNumber, double maxLoadKg, double maxVolumeM3,
        double averageSpeedKmH, decimal baseRatePerKm, Guid id = default)
        : base(registrationNumber, maxLoadKg, maxVolumeM3, averageSpeedKmH, baseRatePerKm, id) { }

    public override TransportConditions Conditions => TransportConditions.Pressurized | TransportConditions.LongRange;

    public override bool CanCarry(Cargo cargo) => base.CanCarry(cargo)
        && !(cargo is DangerousCargo dangerous && dangerous.HazardClass <= 3);

    public override decimal CalculateDeliveryCost(Route route, IReadOnlyCollection<Cargo> cargo) =>
        DistanceCost(route) + (decimal)cargo.Sum(c => c.WeightKg) * PerKgSurcharge;
}

public class CargoShip : Vehicle
{
    public const decimal PortFee = 20000m;

    public const decimal OversizedSurcharge = 500m;

    public CargoShip(string registrationNumber, double maxLoadKg, double maxVolumeM3,
        double averageSpeedKmH, decimal baseRatePerKm, Guid id = default)
        : base(registrationNumber, maxLoadKg, maxVolumeM3, averageSpeedKmH, baseRatePerKm, id) { }

    public override TransportConditions Conditions => TransportConditions.Sealed | TransportConditions.LongRange;

    public override decimal CalculateDeliveryCost(Route route, IReadOnlyCollection<Cargo> cargo) =>
        DistanceCost(route) + PortFee + cargo.OfType<OversizedCargo>().Count() * OversizedSurcharge;
}

public sealed class DroneCourier : Vehicle
{
    public const decimal LaunchFee = 100m;

    public DroneCourier(string registrationNumber, double maxLoadKg, double maxVolumeM3,
        double averageSpeedKmH, decimal baseRatePerKm, double maxRangeKm, Guid id = default)
        : base(registrationNumber, maxLoadKg, maxVolumeM3, averageSpeedKmH, baseRatePerKm, id)
    {
        if (maxLoadKg > 10)
        {
            throw new ArgumentException("Дрон не может поднимать больше 10 кг.");
        }

        MaxRangeKm = maxRangeKm;
    }

    public double MaxRangeKm { get; }

    public override TransportConditions Conditions => TransportConditions.None;

    public override bool CanCarry(Cargo cargo) => base.CanCarry(cargo) && cargo is not DangerousCargo;

    public override bool CanServe(Route route) => route.DistanceKm <= MaxRangeKm;

    public override decimal CalculateDeliveryCost(Route route, IReadOnlyCollection<Cargo> cargo)
    {
        if (!CanServe(route))
        {
            throw new RouteNotFoundException($"Маршрут {route.DistanceKm:F1} км длиннее дальности дрона {MaxRangeKm} км.");
        }

        return DistanceCost(route) + LaunchFee;
    }

    public override string ToString() => $"{base.ToString()}, дальность {MaxRangeKm} км";
}
