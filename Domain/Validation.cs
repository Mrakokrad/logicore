namespace LogiCore.Domain;

public interface IValidator<in T>
{
    ValidationResult Validate(T item);
}

public enum ValidationRule
{
    DangerousWithPerishable,

    TemperatureMismatch,

    CapacityExceeded,

    Expired
}

public class ValidationResult
{
    public ValidationResult(IEnumerable<(ValidationRule Rule, string Message)> errors)
    {
        Errors = errors.ToList().AsReadOnly();
    }

    public IReadOnlyCollection<(ValidationRule Rule, string Message)> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    public bool Has(ValidationRule rule) => Errors.Any(e => e.Rule == rule);

    public void ThrowIfInvalid()
    {
        if (IsValid)
        {
            return;
        }

        var (rule, message) = Errors.First();
        throw rule switch
        {
            ValidationRule.DangerousWithPerishable or ValidationRule.TemperatureMismatch
                => new IncompatibleCargoException(message),
            ValidationRule.CapacityExceeded => new VehicleOverloadException(message),
            _ => new CargoValidationException(message)
        };
    }

    public override string ToString() => IsValid ? "OK" : string.Join("; ", Errors.Select(e => e.Message));
}

public record CargoLoad(IReadOnlyCollection<Cargo> Cargo, Vehicle? Vehicle = null);

public class CargoExpirationValidator : IValidator<Cargo>
{
    private readonly Func<DateTime> _now;

    public CargoExpirationValidator(Func<DateTime> now) => _now = now;

    public ValidationResult Validate(Cargo item)
    {
        var errors = new List<(ValidationRule, string)>();
        if (item is IExpirable expirable && expirable.ExpirationDate < _now())
        {
            errors.Add((ValidationRule.Expired,
                $"У груза «{item.Description}» истёк срок годности ({expirable.ExpirationDate:d})."));
        }

        return new ValidationResult(errors);
    }
}

public class CargoCompatibilityValidator : IValidator<CargoLoad>
{
    private readonly IValidator<Cargo> _expiration;

    public CargoCompatibilityValidator(IValidator<Cargo> expiration) => _expiration = expiration;

    public ValidationResult Validate(CargoLoad item)
    {
        var errors = new List<(ValidationRule Rule, string Message)>();

        foreach (var cargo in item.Cargo)
        {
            errors.AddRange(_expiration.Validate(cargo).Errors);
        }

        if (item.Cargo.Any(c => c is DangerousCargo) && item.Cargo.Any(c => c is PerishableCargo))
        {
            errors.Add((ValidationRule.DangerousWithPerishable,
                "Опасные грузы нельзя перевозить вместе со скоропортящимися."));
        }

        if (item.Vehicle is null)
        {
            return new ValidationResult(errors);
        }

        foreach (var cargo in item.Cargo.OfType<ITemperatureSensitive>())
        {
            bool supported = item.Vehicle is ITemperatureControlled controlled
                             && controlled.SupportsTemperature(cargo.RequiredTemperatureC);
            if (!supported)
            {
                errors.Add((ValidationRule.TemperatureMismatch,
                    $"{item.Vehicle.RegistrationNumber} не обеспечивает {cargo.RequiredTemperatureC} °C."));
            }
        }

        double weight = item.Cargo.Sum(c => c.WeightKg);
        double volume = item.Cargo.Sum(c => c.VolumeM3);
        if (weight > item.Vehicle.MaxLoadKg || volume > item.Vehicle.MaxVolumeM3)
        {
            errors.Add((ValidationRule.CapacityExceeded,
                $"{item.Vehicle.RegistrationNumber}: груз {weight} кг / {volume} м³ " +
                $"при лимите {item.Vehicle.MaxLoadKg} кг / {item.Vehicle.MaxVolumeM3} м³."));
        }

        return new ValidationResult(errors);
    }
}
