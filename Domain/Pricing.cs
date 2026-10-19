namespace LogiCore.Domain;

public sealed class TariffSettings
{
    private static readonly Lazy<TariffSettings> Lazy = new(() => new TariffSettings());

    private TariffSettings() { }

    public static TariffSettings Instance => Lazy.Value;

    public decimal ExpressFactor => 1.5m;

    public double HeavyThresholdKg => 1000;

    public decimal HeavySurchargePerKg => 2m;

    public decimal DefaultInsuranceRate => 0.01m;

    public decimal UrgencyFactor => 1.3m;

    public decimal FragilePackagingFee => 150m;
}

public interface ITariffStrategy
{
    string Name { get; }

    decimal Calculate(decimal baseCost, Route route, IReadOnlyCollection<Cargo> cargo);
}

public class StandardTariff : ITariffStrategy
{
    public string Name => "Стандартный";

    public decimal Calculate(decimal baseCost, Route route, IReadOnlyCollection<Cargo> cargo) => baseCost;
}

public class ExpressTariff : ITariffStrategy
{
    private readonly decimal _factor;

    public ExpressTariff() : this(TariffSettings.Instance.ExpressFactor) { }

    public ExpressTariff(decimal factor) => _factor = factor;

    public string Name => "Экспресс";

    public decimal Calculate(decimal baseCost, Route route, IReadOnlyCollection<Cargo> cargo) => baseCost * _factor;
}

public class HeavyCargoTariff : ITariffStrategy
{
    private readonly double _thresholdKg;
    private readonly decimal _surchargePerKg;

    public HeavyCargoTariff()
        : this(TariffSettings.Instance.HeavyThresholdKg, TariffSettings.Instance.HeavySurchargePerKg) { }

    public HeavyCargoTariff(double thresholdKg, decimal surchargePerKg)
    {
        _thresholdKg = thresholdKg;
        _surchargePerKg = surchargePerKg;
    }

    public string Name => "Тяжеловесный";

    public decimal Calculate(decimal baseCost, Route route, IReadOnlyCollection<Cargo> cargo)
    {
        double extraKg = Math.Max(0, cargo.Sum(c => c.WeightKg) - _thresholdKg);
        return baseCost + (decimal)extraKg * _surchargePerKg;
    }
}

public interface IDeliveryCost
{
    decimal Total { get; }

    string Describe();
}

public class BaseDeliveryCost : IDeliveryCost
{
    private readonly string _description;

    public BaseDeliveryCost(decimal total, string description)
    {
        Total = total;
        _description = description;
    }

    public decimal Total { get; }

    public string Describe() => $"{_description}: {Total:N2}";
}

public abstract class DeliveryCostDecorator : IDeliveryCost
{
    protected DeliveryCostDecorator(IDeliveryCost inner) => Inner = inner;

    protected IDeliveryCost Inner { get; }

    public abstract decimal Total { get; }

    protected abstract string StepName { get; }

    public string Describe() => $"{Inner.Describe()}{Environment.NewLine}{StepName}: {Total:N2}";
}

public class InsuranceDecorator : DeliveryCostDecorator
{
    private readonly IReadOnlyCollection<Cargo> _cargo;
    private readonly decimal _defaultRate;

    public InsuranceDecorator(IDeliveryCost inner, IReadOnlyCollection<Cargo> cargo, decimal defaultRate)
        : base(inner)
    {
        _cargo = cargo;
        _defaultRate = defaultRate;
    }

    public decimal Premium => _cargo.Sum(c =>
        c.DeclaredValue * (c is IInsurable insurable ? insurable.InsuranceRate : _defaultRate));

    public override decimal Total => Inner.Total + Premium;

    protected override string StepName => $"+ страховка {Premium:N2}";
}

public class UrgencyDecorator : DeliveryCostDecorator
{
    private readonly decimal _factor;

    public UrgencyDecorator(IDeliveryCost inner, decimal factor) : base(inner) => _factor = factor;

    public override decimal Total => Inner.Total * _factor;

    protected override string StepName => $"× срочность {_factor}";
}

public class FragilePackagingDecorator : DeliveryCostDecorator
{
    private readonly IReadOnlyCollection<Cargo> _cargo;
    private readonly decimal _feePerItem;

    public FragilePackagingDecorator(IDeliveryCost inner, IReadOnlyCollection<Cargo> cargo, decimal feePerItem)
        : base(inner)
    {
        _cargo = cargo;
        _feePerItem = feePerItem;
    }

    private int FragileCount => _cargo.OfType<FragileCargo>().Count();

    public override decimal Total => Inner.Total + FragileCount * _feePerItem;

    protected override string StepName => $"+ упаковка хрупкого {FragileCount} × {_feePerItem:N2}";
}

public class DeliveryCostCalculator
{
    private readonly TariffSettings _settings;

    public DeliveryCostCalculator(TariffSettings settings) => _settings = settings;

    public IDeliveryCost Calculate(Vehicle vehicle, Route route, IReadOnlyCollection<Cargo> cargo,
        ITariffStrategy tariff, ExtraServices services)
    {
        decimal vehicleCost = vehicle.CalculateDeliveryCost(route, cargo);
        IDeliveryCost cost = new BaseDeliveryCost(tariff.Calculate(vehicleCost, route, cargo),
            $"{vehicle.RegistrationNumber}: перевозка {vehicleCost:N2}, тариф «{tariff.Name}»");

        if (services.HasFlag(ExtraServices.Insurance))
        {
            cost = new InsuranceDecorator(cost, cargo, _settings.DefaultInsuranceRate);
        }

        if (services.HasFlag(ExtraServices.FragilePackaging))
        {
            cost = new FragilePackagingDecorator(cost, cargo, _settings.FragilePackagingFee);
        }

        if (services.HasFlag(ExtraServices.Urgent))
        {
            cost = new UrgencyDecorator(cost, _settings.UrgencyFactor);
        }

        return cost;
    }
}
