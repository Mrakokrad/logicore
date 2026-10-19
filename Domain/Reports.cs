namespace LogiCore.Domain;

public record VehicleRevenueRow(string Number, string Type, int Deliveries, decimal Revenue);

public record StatusRow(OrderStatus Status, int Count, decimal TotalCost);

public record LoadRow(string Type, int Orders, double AverageLoadPercent);

public record CustomerRow(string Customer, int Orders, decimal Total);

public record CargoOrderRow(string Cargo, string OrderId, OrderStatus Status, string Customer);

public class ReportService
{
    private readonly IReadOnlyRepository<Order> _orders;
    private readonly IReadOnlyRepository<Customer> _customers;

    public ReportService(IReadOnlyRepository<Order> orders, IReadOnlyRepository<Customer> customers)
    {
        _orders = orders;
        _customers = customers;
    }

    public IReadOnlyList<VehicleRevenueRow> TopVehiclesByRevenue(int count) => _orders.GetAll()
        .Where(o => o.Status == OrderStatus.Delivered && o.AssignedVehicle is not null)
        .GroupBy(o => o.AssignedVehicle!)
        .Select(g => new VehicleRevenueRow(g.Key.RegistrationNumber, g.Key.GetType().Name,
            g.Count(), g.Sum(o => o.TotalCost)))
        .OrderByDescending(r => r.Revenue)
        .Take(count)
        .ToList();

    public IReadOnlyList<StatusRow> OrdersByStatus() => _orders.GetAll()
        .ToLookup(o => o.Status)
        .Select(g => new StatusRow(g.Key, g.Count(), g.Sum(o => o.TotalCost)))
        .OrderBy(r => r.Status)
        .ToList();

    public IReadOnlyList<LoadRow> AverageLoadByVehicleType() => _orders.GetAll()
        .Where(o => o.AssignedVehicle is not null && o.Status != OrderStatus.Cancelled)
        .GroupBy(o => o.AssignedVehicle!.GetType().Name)
        .Select(g => new LoadRow(g.Key, g.Count(),
            g.Average(o => o.TotalWeightKg / o.AssignedVehicle!.MaxLoadKg * 100)))
        .OrderByDescending(r => r.AverageLoadPercent)
        .ToList();

    public IReadOnlyList<CustomerRow> CustomersAbove(decimal threshold) => _customers.GetAll()
        .Select(c => new CustomerRow(c.Name, c.Orders.Count,
            c.Orders.Where(o => o.Status != OrderStatus.Cancelled).Sum(o => o.TotalCost)))
        .Where(r => r.Total > threshold)
        .OrderByDescending(r => r.Total)
        .ToList();

    public IReadOnlyList<CargoOrderRow> CargoOrderCustomer() =>
        (from order in _orders.GetAll()
         from cargo in order.Cargo
         join customer in _customers.GetAll() on order.Customer.Id equals customer.Id
         orderby customer.Name, cargo.Description
         select new CargoOrderRow(cargo.Description, order.ShortId, order.Status, customer.Name))
        .ToList();

    public IReadOnlyDictionary<int, int> DangerousByHazardClass() => _orders.GetAll()
        .SelectMany(o => o.Cargo)
        .OfType<DangerousCargo>()
        .GroupBy(c => c.HazardClass)
        .OrderBy(g => g.Key)
        .ToDictionary(g => g.Key, g => g.Count());
}
