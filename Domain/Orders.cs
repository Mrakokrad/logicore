namespace LogiCore.Domain;

public enum OrderStatus
{
    Created,
    Assigned,
    InTransit,
    Delivered,
    Cancelled
}

[Flags]
public enum ExtraServices
{
    None = 0,
    Insurance = 1,
    Urgent = 2,
    FragilePackaging = 4
}

public readonly struct RoutePoint
{
    public RoutePoint(string name, double latitude, double longitude)
    {
        Name = name;
        Latitude = latitude;
        Longitude = longitude;
    }

    public string Name { get; }

    public double Latitude { get; }

    public double Longitude { get; }

    public static double operator -(RoutePoint a, RoutePoint b)
    {
        double lat1 = a.Latitude * Math.PI / 180;
        double lat2 = b.Latitude * Math.PI / 180;
        double deltaLat = lat2 - lat1;
        double deltaLon = (b.Longitude - a.Longitude) * Math.PI / 180;

        double h = Math.Pow(Math.Sin(deltaLat / 2), 2)
                   + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(deltaLon / 2), 2);
        return 2 * 6371.0 * Math.Asin(Math.Sqrt(h));
    }

    public static explicit operator string(RoutePoint point) => point.ToString();

    public override string ToString() => $"{Name} ({Latitude:F2}; {Longitude:F2})";
}

public class Route
{
    private readonly List<RoutePoint> _points;

    public Route(IEnumerable<RoutePoint> points)
    {
        _points = points.ToList();
        if (_points.Count < 2)
        {
            throw new RouteNotFoundException("Маршрут должен содержать минимум две точки.");
        }
    }

    public IReadOnlyList<RoutePoint> Points => _points.AsReadOnly();

    public double DistanceKm
    {
        get
        {
            double total = 0;
            for (int i = 1; i < _points.Count; i++)
            {
                total += _points[i] - _points[i - 1];
            }

            return total;
        }
    }

    public TimeSpan EstimateTime(Vehicle vehicle) => TimeSpan.FromHours(DistanceKm / vehicle.AverageSpeedKmH);

    public override string ToString() => $"{string.Join(" → ", _points.Select(p => p.Name))} ({DistanceKm:F0} км)";
}

public class Customer : IEntity
{
    private readonly List<Order> _orders = new();

    public Customer(string name, string contact, Guid id = default)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Name = name;
        Contact = contact;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string Contact { get; }

    public IReadOnlyCollection<Order> Orders => _orders.AsReadOnly();

    public override string ToString() => $"{Name} ({Contact}), заказов: {_orders.Count}";

    internal void AddOrder(Order order) => _orders.Add(order);
}

public class Order : IEntity
{
    private readonly List<Cargo> _cargo;

    public Order(Customer customer, IEnumerable<Cargo> cargo, Route route,
        ExtraServices services = ExtraServices.None, Guid id = default)
    {
        _cargo = cargo.ToList();
        if (_cargo.Count == 0)
        {
            throw new CargoValidationException("Заказ должен содержать хотя бы один груз.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Customer = customer;
        Route = route;
        Services = services;
        customer.AddOrder(this);
    }

    public Guid Id { get; }

    public string ShortId => Id.ToString()[..8];

    public Customer Customer { get; }

    public IReadOnlyCollection<Cargo> Cargo => _cargo.AsReadOnly();

    public Route Route { get; }

    public ExtraServices Services { get; }

    public Vehicle? AssignedVehicle { get; private set; }

    public decimal TotalCost { get; private set; }

    public OrderStatus Status { get; private set; } = OrderStatus.Created;

    public double TotalWeightKg => _cargo.Sum(c => c.WeightKg);

    public double TotalVolumeM3 => _cargo.Sum(c => c.VolumeM3);

    public void Assign(Vehicle vehicle, decimal totalCost)
    {
        EnsureStatus(nameof(Assign), OrderStatus.Created);
        AssignedVehicle = vehicle;
        TotalCost = totalCost;
        Status = OrderStatus.Assigned;
    }

    public void StartDelivery()
    {
        EnsureStatus(nameof(StartDelivery), OrderStatus.Assigned);
        Status = OrderStatus.InTransit;
    }

    public void Complete()
    {
        EnsureStatus(nameof(Complete), OrderStatus.InTransit);
        Status = OrderStatus.Delivered;
    }

    public void Cancel()
    {
        EnsureStatus(nameof(Cancel), OrderStatus.Created, OrderStatus.Assigned);
        Status = OrderStatus.Cancelled;
    }

    public override string ToString() =>
        $"Заказ {ShortId} [{Status}] {Customer.Name}: {Route}, {TotalCost:N2} руб." +
        (AssignedVehicle is null ? string.Empty : $", ТС {AssignedVehicle.RegistrationNumber}");

    internal void Restore(OrderStatus status, Vehicle? vehicle, decimal totalCost)
    {
        Status = status;
        AssignedVehicle = vehicle;
        TotalCost = totalCost;
    }

    private void EnsureStatus(string action, params OrderStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new InvalidOrderStateException(Id, Status, action);
        }
    }
}
