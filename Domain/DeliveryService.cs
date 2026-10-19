namespace LogiCore.Domain;

public class OrderCreatedEventArgs : EventArgs
{
    public OrderCreatedEventArgs(Order order) => Order = order;

    public Order Order { get; }
}

public class OrderStatusChangedEventArgs : EventArgs
{
    public OrderStatusChangedEventArgs(Order order, OrderStatus oldStatus, OrderStatus newStatus)
    {
        Order = order;
        OldStatus = oldStatus;
        NewStatus = newStatus;
    }

    public Order Order { get; }

    public OrderStatus OldStatus { get; }

    public OrderStatus NewStatus { get; }
}

public class VehicleOverloadEventArgs : EventArgs
{
    public VehicleOverloadEventArgs(Vehicle vehicle, double requestedKg, double requestedM3)
    {
        Vehicle = vehicle;
        RequestedKg = requestedKg;
        RequestedM3 = requestedM3;
    }

    public Vehicle Vehicle { get; }

    public double RequestedKg { get; }

    public double RequestedM3 { get; }
}

public class DeliveryCompletedEventArgs : EventArgs
{
    public DeliveryCompletedEventArgs(Order order, decimal totalRevenue)
    {
        Order = order;
        TotalRevenue = totalRevenue;
    }

    public Order Order { get; }

    public decimal TotalRevenue { get; }
}

public delegate void OrderCreatedHandler(object? sender, OrderCreatedEventArgs e);

public interface IDeliveryEvents
{
    event OrderCreatedHandler? OrderCreated;

    event EventHandler<OrderStatusChangedEventArgs>? OrderStatusChanged;

    event EventHandler<VehicleOverloadEventArgs>? VehicleOverloadAttempt;

    event EventHandler<DeliveryCompletedEventArgs>? DeliveryCompleted;
}

public class CompanyState
{
    public Repository<Vehicle> Fleet { get; } = new();

    public Repository<Customer> Customers { get; } = new();

    public Repository<Order> Orders { get; } = new();

    public void Clear()
    {
        Orders.Clear();
        Customers.Clear();
        Fleet.Clear();
    }
}

public class DeliveryService : IDeliveryEvents
{
    private readonly Repository<Vehicle> _fleet;
    private readonly Repository<Order> _orders;
    private readonly IValidator<CargoLoad> _validator;
    private readonly DeliveryCostCalculator _calculator;

    public DeliveryService(Repository<Vehicle> fleet, Repository<Order> orders,
        IValidator<CargoLoad> validator, DeliveryCostCalculator calculator)
    {
        _fleet = fleet;
        _orders = orders;
        _validator = validator;
        _calculator = calculator;
    }

    public event OrderCreatedHandler? OrderCreated;

    public event EventHandler<OrderStatusChangedEventArgs>? OrderStatusChanged;

    public event EventHandler<VehicleOverloadEventArgs>? VehicleOverloadAttempt;

    public event EventHandler<DeliveryCompletedEventArgs>? DeliveryCompleted;

    public decimal TotalRevenue => _orders.Where(o => o.Status == OrderStatus.Delivered).Sum(o => o.TotalCost);

    public Order PlaceOrder(Customer customer, IReadOnlyCollection<Cargo> cargo, Route route,
        ExtraServices services = ExtraServices.None)
    {
        _validator.Validate(new CargoLoad(cargo)).ThrowIfInvalid();

        var order = new Order(customer, cargo, route, services);
        _orders.Add(order);
        OrderCreated?.Invoke(this, new OrderCreatedEventArgs(order));
        return order;
    }

    public IReadOnlyList<Vehicle> FindSuitableVehicles(Order order) => _fleet
        .Where(v => v.State == VehicleState.Free
                    && v.CanServe(order.Route)
                    && order.Cargo.All(v.CanCarry)
                    && _validator.Validate(new CargoLoad(order.Cargo, v)).IsValid)
        .ToList();

    public IDeliveryCost Quote(Order order, Vehicle vehicle, ITariffStrategy tariff) =>
        _calculator.Calculate(vehicle, order.Route, order.Cargo, tariff, order.Services);

    public IDeliveryCost AssignVehicle(Order order, Vehicle vehicle, ITariffStrategy tariff)
    {
        var result = _validator.Validate(new CargoLoad(order.Cargo, vehicle));
        if (result.Has(ValidationRule.CapacityExceeded))
        {
            VehicleOverloadAttempt?.Invoke(this,
                new VehicleOverloadEventArgs(vehicle, order.TotalWeightKg, order.TotalVolumeM3));
        }

        result.ThrowIfInvalid();

        if (!order.Cargo.All(vehicle.CanCarry))
        {
            throw new IncompatibleCargoException($"ТС {vehicle.RegistrationNumber} не может везти один из грузов.");
        }

        var cost = Quote(order, vehicle, tariff);
        ChangeStatus(order, () => order.Assign(vehicle, cost.Total));
        return cost;
    }

    public IDeliveryCost? AssignCheapestVehicle(Order order, ITariffStrategy tariff)
    {
        var vehicle = FindSuitableVehicles(order).OrderBy(v => Quote(order, v, tariff).Total).FirstOrDefault();
        return vehicle is null ? null : AssignVehicle(order, vehicle, tariff);
    }

    public void StartDelivery(Order order)
    {
        ChangeStatus(order, order.StartDelivery);
        order.AssignedVehicle!.SetState(VehicleState.InTransit);
    }

    public void CompleteDelivery(Order order)
    {
        ChangeStatus(order, order.Complete);
        order.AssignedVehicle!.SetState(VehicleState.Free);
        DeliveryCompleted?.Invoke(this, new DeliveryCompletedEventArgs(order, TotalRevenue));
    }

    public void CancelOrder(Order order) => ChangeStatus(order, order.Cancel);

    private void ChangeStatus(Order order, Action transition)
    {
        var oldStatus = order.Status;
        transition();
        OrderStatusChanged?.Invoke(this, new OrderStatusChangedEventArgs(order, oldStatus, order.Status));
    }
}
