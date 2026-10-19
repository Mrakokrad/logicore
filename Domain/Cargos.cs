using System.Text.Json.Serialization;

namespace LogiCore.Domain;

public interface ITemperatureSensitive
{
    double RequiredTemperatureC { get; }
}

public interface IExpirable
{
    DateTime ExpirationDate { get; }
}

public interface IInsurable
{
    decimal InsuranceRate { get; }
}

public interface IStackable
{
    int MaxStackLayers { get; }
}

[JsonDerivedType(typeof(StandardCargo), "standard")]
[JsonDerivedType(typeof(PerishableCargo), "perishable")]
[JsonDerivedType(typeof(FragileCargo), "fragile")]
[JsonDerivedType(typeof(DangerousCargo), "dangerous")]
[JsonDerivedType(typeof(OversizedCargo), "oversized")]
public abstract class Cargo : IEntity
{
    protected Cargo(string description, double weightKg, double volumeM3, decimal declaredValue, Guid id = default)
    {
        if (weightKg <= 0 || volumeM3 <= 0)
        {
            throw new CargoValidationException($"Вес и объём груза «{description}» должны быть больше нуля.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Description = description;
        WeightKg = weightKg;
        VolumeM3 = volumeM3;
        DeclaredValue = declaredValue;
    }

    public Guid Id { get; }

    public string Description { get; }

    public double WeightKg { get; }

    public double VolumeM3 { get; }

    public decimal DeclaredValue { get; }

    [JsonIgnore]
    public abstract string Category { get; }

    public override bool Equals(object? obj) => obj is Cargo other && other.Id == Id;

    public override int GetHashCode() => Id.GetHashCode();

    public override string ToString() => $"{Category} «{Description}», {WeightKg} кг, {VolumeM3} м³";
}

public class StandardCargo : Cargo, IStackable
{
    public StandardCargo(string description, double weightKg, double volumeM3, decimal declaredValue, Guid id = default)
        : base(description, weightKg, volumeM3, declaredValue, id) { }

    public override string Category => "Обычный";

    public int MaxStackLayers => 3;
}

public class PerishableCargo : Cargo, ITemperatureSensitive, IExpirable
{
    public PerishableCargo(string description, double weightKg, double volumeM3, decimal declaredValue,
        DateTime expirationDate, double requiredTemperatureC, Guid id = default)
        : base(description, weightKg, volumeM3, declaredValue, id)
    {
        ExpirationDate = expirationDate;
        RequiredTemperatureC = requiredTemperatureC;
    }

    public DateTime ExpirationDate { get; }

    public double RequiredTemperatureC { get; }

    public override string Category => "Скоропортящийся";

    public override string ToString() => $"{base.ToString()}, {RequiredTemperatureC} °C, годен до {ExpirationDate:d}";
}

public class FragileCargo : Cargo, IInsurable
{
    public FragileCargo(string description, double weightKg, double volumeM3, decimal declaredValue,
        double riskFactor, Guid id = default)
        : base(description, weightKg, volumeM3, declaredValue, id)
    {
        if (riskFactor < 1 || riskFactor > 5)
        {
            throw new CargoValidationException("Коэффициент риска должен быть от 1 до 5.");
        }

        RiskFactor = riskFactor;
    }

    public double RiskFactor { get; }

    public override string Category => "Хрупкий";

    [JsonIgnore]
    decimal IInsurable.InsuranceRate => 0.02m * (decimal)RiskFactor;
}

public class DangerousCargo : Cargo, IInsurable
{
    public DangerousCargo(string description, double weightKg, double volumeM3, decimal declaredValue,
        int hazardClass, Guid id = default)
        : base(description, weightKg, volumeM3, declaredValue, id)
    {
        if (hazardClass < 1 || hazardClass > 9)
        {
            throw new CargoValidationException($"Класс опасности должен быть от 1 до 9 (получено {hazardClass}).");
        }

        HazardClass = hazardClass;
    }

    public int HazardClass { get; }

    public override string Category => "Опасный";

    [JsonIgnore]
    public decimal InsuranceRate => 0.05m;

    public override string ToString() => $"{base.ToString()}, класс опасности {HazardClass}";
}

public class OversizedCargo : Cargo
{
    public OversizedCargo(string description, double weightKg, double volumeM3, decimal declaredValue,
        double lengthM, Guid id = default)
        : base(description, weightKg, volumeM3, declaredValue, id)
    {
        LengthM = lengthM;
    }

    public double LengthM { get; }

    public override string Category => "Негабаритный";
}
