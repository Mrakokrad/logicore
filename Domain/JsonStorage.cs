using System.Text.Json;
using System.Text.Json.Serialization;

namespace LogiCore.Domain;

public class JsonStorage
{
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public void Save(CompanyState state, string path)
    {
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, Snapshot.From(state), _options);
    }

    public bool Load(string path, CompanyState target)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            Snapshot? snapshot;
            using (var stream = File.OpenRead(path))
            {
                snapshot = JsonSerializer.Deserialize<Snapshot>(stream, _options);
            }

            snapshot!.RestoreInto(target);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException
                                       or NullReferenceException or KeyNotFoundException or LogisticsException)
        {
            throw new StateStorageException($"Файл «{path}» повреждён: {ex.Message}", ex);
        }
    }

    private class Snapshot
    {
        public List<Vehicle> Fleet { get; set; } = new();

        public List<CustomerDto> Customers { get; set; } = new();

        public List<OrderDto> Orders { get; set; } = new();

        public static Snapshot From(CompanyState state) => new()
        {
            Fleet = state.Fleet.ToList(),
            Customers = state.Customers.Select(c => new CustomerDto(c.Id, c.Name, c.Contact)).ToList(),
            Orders = state.Orders.Select(OrderDto.From).ToList()
        };

        public void RestoreInto(CompanyState target)
        {
            var customers = Customers.ToDictionary(c => c.Id, c => new Customer(c.Name, c.Contact, c.Id));
            var vehicles = Fleet.ToDictionary(v => v.Id);
            var orders = Orders.Select(o => o.ToOrder(customers, vehicles)).ToList();

            target.Clear();
            foreach (var vehicle in Fleet)
            {
                target.Fleet.Add(vehicle);
            }

            foreach (var customer in customers.Values)
            {
                target.Customers.Add(customer);
            }

            foreach (var order in orders)
            {
                target.Orders.Add(order);
            }
        }
    }

    private record CustomerDto(Guid Id, string Name, string Contact);

    private record PointDto(string Name, double Latitude, double Longitude);

    private record OrderDto(Guid Id, Guid CustomerId, Guid? VehicleId, List<Cargo> Cargo,
        List<PointDto> Route, ExtraServices Services, OrderStatus Status, decimal TotalCost)
    {
        public static OrderDto From(Order order) => new(
            order.Id,
            order.Customer.Id,
            order.AssignedVehicle?.Id,
            order.Cargo.ToList(),
            order.Route.Points.Select(p => new PointDto(p.Name, p.Latitude, p.Longitude)).ToList(),
            order.Services,
            order.Status,
            order.TotalCost);

        public Order ToOrder(Dictionary<Guid, Customer> customers, Dictionary<Guid, Vehicle> vehicles)
        {
            var route = new Route(Route.Select(p => new RoutePoint(p.Name, p.Latitude, p.Longitude)));
            var order = new Order(customers[CustomerId], Cargo, route, Services, Id);
            order.Restore(Status, VehicleId is null ? null : vehicles[VehicleId.Value], TotalCost);
            return order;
        }
    }
}
