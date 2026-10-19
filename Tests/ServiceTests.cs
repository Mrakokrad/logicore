using LogiCore.Domain;
using Xunit;

namespace LogiCore.Tests;

public class ValidationTests
{
    private readonly CargoCompatibilityValidator _validator = TestData.Validator();

    [Fact]
    public void Rule1DangerousWithPerishableIsRejected()
    {
        var result = _validator.Validate(new CargoLoad(new Cargo[] { TestData.Dangerous(), TestData.Perishable() }));

        Assert.True(result.Has(ValidationRule.DangerousWithPerishable));
        Assert.Throws<IncompatibleCargoException>(() => result.ThrowIfInvalid());
    }

    [Fact]
    public void Rule2PerishableNeedsSuitableTemperature()
    {
        var milk = new Cargo[] { TestData.Perishable(temperatureC: 4) };
        var deepFrozen = new Cargo[] { TestData.Perishable(temperatureC: -40) };

        Assert.True(_validator.Validate(new CargoLoad(milk, TestData.Truck())).Has(ValidationRule.TemperatureMismatch));
        Assert.True(_validator.Validate(new CargoLoad(deepFrozen, TestData.Reefer())).Has(ValidationRule.TemperatureMismatch));
        Assert.True(_validator.Validate(new CargoLoad(milk, TestData.Reefer())).IsValid);
    }

    [Fact]
    public void Rule3TotalWeightAndVolumeMustFit()
    {
        var truck = TestData.Truck(maxLoadKg: 1000, maxVolumeM3: 2.5);
        var fits = new Cargo[] { TestData.Standard(500), TestData.Standard(500) };
        var tooHeavy = new Cargo[] { TestData.Standard(600), TestData.Standard(600) };
        var tooBig = new Cargo[] { TestData.Standard(10), TestData.Standard(10), TestData.Standard(10) };

        Assert.True(_validator.Validate(new CargoLoad(fits, truck)).IsValid);
        Assert.True(_validator.Validate(new CargoLoad(tooHeavy, truck)).Has(ValidationRule.CapacityExceeded));
        Assert.True(_validator.Validate(new CargoLoad(tooBig, truck)).Has(ValidationRule.CapacityExceeded));
    }

    [Fact]
    public void Rule4ExpiredCargoIsRejected()
    {
        var result = _validator.Validate(new CargoLoad(new Cargo[] { TestData.Perishable(daysLeft: -1) }));

        Assert.True(result.Has(ValidationRule.Expired));
        Assert.Throws<CargoValidationException>(() => result.ThrowIfInvalid());
    }

    [Fact]
    public void ValidatorIsContravariant()
    {
        IValidator<Cargo> anyCargo = new CargoExpirationValidator(() => TestData.Today);
        IValidator<PerishableCargo> perishableOnly = anyCargo;

        Assert.False(perishableOnly.Validate(TestData.Perishable(daysLeft: -3)).IsValid);
        Assert.True(perishableOnly.Validate(TestData.Perishable(daysLeft: 3)).IsValid);
    }
}

public class OrderStateTests
{
    [Fact]
    public void FullLifecycleGoesThroughAllStatuses()
    {
        var order = TestData.Order(TestData.Standard());
        var truck = TestData.Truck();

        Assert.Equal(OrderStatus.Created, order.Status);
        order.Assign(truck, 1000m);
        Assert.Equal(OrderStatus.Assigned, order.Status);
        order.StartDelivery();
        Assert.Equal(OrderStatus.InTransit, order.Status);
        order.Complete();
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(1000m, order.TotalCost);
    }

    [Theory]
    [InlineData(OrderStatus.Created)]
    [InlineData(OrderStatus.Assigned)]
    public void CancelIsAllowedFromCreatedAndAssigned(OrderStatus status)
    {
        var order = OrderIn(status);

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Created, "StartDelivery")]
    [InlineData(OrderStatus.Created, "Complete")]
    [InlineData(OrderStatus.Assigned, "Assign")]
    [InlineData(OrderStatus.InTransit, "Cancel")]
    [InlineData(OrderStatus.Delivered, "Cancel")]
    [InlineData(OrderStatus.Cancelled, "StartDelivery")]
    public void InvalidTransitionThrowsAndKeepsStatus(OrderStatus status, string action)
    {
        var order = OrderIn(status);
        Action transition = action switch
        {
            "Assign" => () => order.Assign(TestData.Truck(), 1m),
            "StartDelivery" => order.StartDelivery,
            "Complete" => order.Complete,
            _ => order.Cancel
        };

        var ex = Assert.Throws<InvalidOrderStateException>(transition);
        Assert.Equal(status, ex.CurrentStatus);
        Assert.Equal(status, order.Status);
    }

    [Fact]
    public void CustomerSeesOrderInReadOnlyHistory()
    {
        var customer = TestData.Customer();
        var order = new Order(customer, new Cargo[] { TestData.Standard() }, TestData.ShortRoute);

        Assert.Contains(order, customer.Orders);
        Assert.IsNotType<List<Order>>(customer.Orders);
    }

    private static Order OrderIn(OrderStatus status)
    {
        var order = TestData.Order(TestData.Standard());
        if (status == OrderStatus.Cancelled)
        {
            order.Cancel();
            return order;
        }

        if (status >= OrderStatus.Assigned)
        {
            order.Assign(TestData.Truck(), 100m);
        }

        if (status >= OrderStatus.InTransit)
        {
            order.StartDelivery();
        }

        if (status >= OrderStatus.Delivered)
        {
            order.Complete();
        }

        return order;
    }
}

public class PricingTests
{
    private readonly Cargo[] _cargo = { TestData.Standard(value: 100000m), TestData.Fragile(riskFactor: 2) };

    [Fact]
    public void ThreeDecoratorsAreAppliedInChain()
    {
        IDeliveryCost cost = new BaseDeliveryCost(10000m, "База");
        cost = new InsuranceDecorator(cost, _cargo, 0.01m);
        cost = new FragilePackagingDecorator(cost, _cargo, 200m);
        cost = new UrgencyDecorator(cost, 1.5m);

        decimal premium = 100000m * 0.01m + 10000m * 0.04m;
        Assert.Equal((10000m + premium + 200m) * 1.5m, cost.Total);
        Assert.Equal(4, cost.Describe().Split(Environment.NewLine).Length);
    }

    [Fact]
    public void DecoratorOrderChangesTotal()
    {
        IDeliveryCost basic = new BaseDeliveryCost(10000m, "База");

        var packThenUrgent = new UrgencyDecorator(new FragilePackagingDecorator(basic, _cargo, 200m), 1.5m);
        var urgentThenPack = new FragilePackagingDecorator(new UrgencyDecorator(basic, 1.5m), _cargo, 200m);

        Assert.Equal((10000m + 200m) * 1.5m, packThenUrgent.Total);
        Assert.Equal(10000m * 1.5m + 200m, urgentThenPack.Total);
    }

    [Fact]
    public void InsuranceUsesExplicitRateOfFragileCargo()
    {
        var fragile = TestData.Fragile(riskFactor: 3);
        IInsurable insurable = fragile;

        var cost = new InsuranceDecorator(new BaseDeliveryCost(0m, "База"), new Cargo[] { fragile }, 0.01m);

        Assert.Equal(0.06m, insurable.InsuranceRate);
        Assert.Equal(fragile.DeclaredValue * 0.06m, cost.Total);
    }

    [Fact]
    public void TariffStrategiesChangeOnlyTheirPart()
    {
        var heavy = new Cargo[] { TestData.Standard(1500) };

        Assert.Equal(10000m, new StandardTariff().Calculate(10000m, TestData.ShortRoute, heavy));
        Assert.Equal(20000m, new ExpressTariff(2m).Calculate(10000m, TestData.ShortRoute, heavy));
        Assert.Equal(11500m, new HeavyCargoTariff(1000, 3m).Calculate(10000m, TestData.ShortRoute, heavy));
    }

    [Fact]
    public void TariffSettingsIsSingleton()
    {
        Assert.Same(TariffSettings.Instance, TariffSettings.Instance);
    }
}

public class DeliveryServiceTests
{
    private readonly CompanyState _state = new();
    private readonly DeliveryService _service;

    public DeliveryServiceTests()
    {
        _service = new DeliveryService(_state.Fleet, _state.Orders, TestData.Validator(),
            new DeliveryCostCalculator(TariffSettings.Instance));
    }

    [Fact]
    public void PlaceOrderWithIncompatibleCargoThrowsAndSavesNothing()
    {
        var cargo = new Cargo[] { TestData.Dangerous(), TestData.Perishable() };

        Assert.Throws<IncompatibleCargoException>(() =>
            _service.PlaceOrder(TestData.Customer(), cargo, TestData.ShortRoute));
        Assert.Equal(0, _state.Orders.Count);
    }

    [Fact]
    public void AssignCheapestVehiclePicksCheapestSuitable()
    {
        var expensive = new Truck("TR-0001", 10000, 50, 80, 50m);
        var cheap = new Truck("TR-0002", 10000, 50, 80, 20m);
        _state.Fleet.Add(expensive);
        _state.Fleet.Add(cheap);
        _state.Fleet.Add(TestData.Drone());
        var order = Place(TestData.Standard(500));

        var cost = _service.AssignCheapestVehicle(order, new StandardTariff());

        Assert.NotNull(cost);
        Assert.Same(cheap, order.AssignedVehicle);
        Assert.Equal(cost!.Total, order.TotalCost);
    }

    [Fact]
    public void OverloadRaisesEventAndThrows()
    {
        var drone = TestData.Drone();
        _state.Fleet.Add(drone);
        var order = Place(TestData.Standard(100));
        VehicleOverloadEventArgs? raised = null;
        _service.VehicleOverloadAttempt += (_, e) => raised = e;

        Assert.Throws<VehicleOverloadException>(() => _service.AssignVehicle(order, drone, new StandardTariff()));
        Assert.NotNull(raised);
        Assert.Equal(100, raised!.RequestedKg);
        Assert.Equal(OrderStatus.Created, order.Status);
    }

    [Fact]
    public void LifecycleChangesVehicleStateRaisesEventsAndCountsRevenue()
    {
        var truck = TestData.Truck();
        _state.Fleet.Add(truck);
        var statuses = new List<OrderStatus>();
        DeliveryCompletedEventArgs? completed = null;
        _service.OrderStatusChanged += (_, e) => statuses.Add(e.NewStatus);
        _service.DeliveryCompleted += (_, e) => completed = e;
        var order = Place(TestData.Standard());

        _service.AssignCheapestVehicle(order, new StandardTariff());
        _service.StartDelivery(order);
        Assert.Equal(VehicleState.InTransit, truck.State);
        _service.CompleteDelivery(order);

        Assert.Equal(VehicleState.Free, truck.State);
        Assert.Equal(new[] { OrderStatus.Assigned, OrderStatus.InTransit, OrderStatus.Delivered }, statuses);
        Assert.Equal(order.TotalCost, _service.TotalRevenue);
        Assert.Equal(order.TotalCost, completed!.TotalRevenue);
    }

    [Fact]
    public void UnsubscribedHandlerIsNotCalled()
    {
        int calls = 0;
        void Handler(object? sender, OrderCreatedEventArgs e) => calls++;

        _service.OrderCreated += Handler;
        Place(TestData.Standard());
        _service.OrderCreated -= Handler;
        Place(TestData.Standard());

        Assert.Equal(1, calls);
    }

    private Order Place(params Cargo[] cargo) =>
        _service.PlaceOrder(TestData.Customer(), cargo, TestData.ShortRoute);
}

public class StorageTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"logicore_{Guid.NewGuid():N}.json");
    private readonly JsonStorage _storage = new();

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void SaveThenLoadRestoresEquivalentState()
    {
        var original = BuildState();
        _storage.Save(original, _path);

        var loaded = new CompanyState();
        bool ok = _storage.Load(_path, loaded);

        Assert.True(ok);
        Assert.Equal(original.Fleet.Select(Describe), loaded.Fleet.Select(Describe));
        Assert.Equal(original.Orders.Select(Describe), loaded.Orders.Select(Describe));
        Assert.Equal(original.Customers.Select(c => c.Name), loaded.Customers.Select(c => c.Name));

        var reefer = Assert.IsType<RefrigeratorTruck>(loaded.Fleet.Single(v => v.RegistrationNumber == "RF-0001"));
        Assert.Equal(-25, reefer.MinTemperatureC);
        var order = loaded.Orders.Single(o => o.Status == OrderStatus.InTransit);
        Assert.Same(loaded.Fleet[order.AssignedVehicle!.Id], order.AssignedVehicle);
        Assert.IsType<PerishableCargo>(order.Cargo.First());
    }

    [Fact]
    public void MissingFileReturnsFalse()
    {
        Assert.False(_storage.Load(_path, new CompanyState()));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"Fleet\": [ { \"$type\": \"submarine\" } ] }")]
    public void CorruptedFileThrowsAndKeepsState(string content)
    {
        File.WriteAllText(_path, content);
        var state = BuildState();

        Assert.Throws<StateStorageException>(() => _storage.Load(_path, state));
        Assert.Equal(2, state.Orders.Count);
    }

    private static CompanyState BuildState()
    {
        var state = new CompanyState();
        var reefer = TestData.Reefer();
        state.Fleet.Add(reefer);
        state.Fleet.Add(TestData.Ship());
        var customer = TestData.Customer();
        state.Customers.Add(customer);

        var inTransit = new Order(customer, new Cargo[] { TestData.Perishable(), TestData.Fragile() },
            TestData.ShortRoute, ExtraServices.Insurance);
        inTransit.Assign(reefer, 12345.67m);
        inTransit.StartDelivery();
        state.Orders.Add(inTransit);
        state.Orders.Add(new Order(customer, new Cargo[] { TestData.Oversized() }, TestData.ShortRoute));
        return state;
    }

    private static string Describe(Vehicle v) => $"{v.GetType().Name}|{v}|{v.Id}";

    private static string Describe(Order o) =>
        $"{o.Id}|{o.Status}|{o.TotalCost}|{o.Services}|{o.Route}|{o.AssignedVehicle?.Id}|" +
        string.Join(",", o.Cargo.Select(c => $"{c.GetType().Name}:{c.Id}:{c}"));
}
