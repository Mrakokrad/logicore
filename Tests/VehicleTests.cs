using LogiCore.Domain;
using Xunit;

namespace LogiCore.Tests;

internal static class TestData
{
    public static readonly DateTime Today = new(2026, 1, 15);

    public static Route ShortRoute => new(new[] { new RoutePoint("A", 0, 0), new RoutePoint("B", 0, 1) });

    public static Route ZeroRoute => new(new[] { new RoutePoint("A", 0, 0), new RoutePoint("A", 0, 0) });

    public static Truck Truck(double maxLoadKg = 10000, double maxVolumeM3 = 50) =>
        new("TR-0001", maxLoadKg, maxVolumeM3, 80, 30m);

    public static RefrigeratorTruck Reefer() => new("RF-0001", 10000, 50, 75, 40m, -25, 8);

    public static CargoPlane Plane() => new("AV-0001", 40000, 300, 800, 120m);

    public static CargoShip Ship() => new("SH-0001", 500000, 5000, 35, 8m);

    public static DroneCourier Drone() => new("DR-0001", 5, 0.05, 60, 15m, 30);

    public static StandardCargo Standard(double weightKg = 100, decimal value = 1000m) =>
        new("Коробки", weightKg, 1, value);

    public static PerishableCargo Perishable(double temperatureC = 4, int daysLeft = 5) =>
        new("Молоко", 200, 1, 5000m, Today.AddDays(daysLeft), temperatureC);

    public static FragileCargo Fragile(double riskFactor = 2) => new("Стекло", 50, 0.5, 10000m, riskFactor);

    public static DangerousCargo Dangerous(int hazardClass = 8) => new("Кислота", 100, 0.5, 2000m, hazardClass);

    public static OversizedCargo Oversized() => new("Турбина", 5000, 40, 100000m, 12);

    public static Customer Customer() => new("Клиент", "test@example.com");

    public static Order Order(params Cargo[] cargo) => new(Customer(), cargo, ShortRoute);

    public static CargoCompatibilityValidator Validator() => new(new CargoExpirationValidator(() => Today));
}

public class VehicleTests
{
    private readonly Cargo[] _cargo = { TestData.Standard(100) };

    [Fact]
    public void TruckCostIsDistanceTimesRateTimesTollFactor()
    {
        var route = TestData.ShortRoute;
        decimal expected = (decimal)route.DistanceKm * 30m * Truck.TollRoadFactor;

        Assert.Equal(expected, TestData.Truck().CalculateDeliveryCost(route, _cargo));
    }

    [Fact]
    public void RefrigeratorCostAddsRefrigerationFactor()
    {
        var route = TestData.ShortRoute;
        decimal expected = (decimal)route.DistanceKm * 40m * Truck.TollRoadFactor * RefrigeratorTruck.RefrigerationFactor;

        Assert.Equal(expected, TestData.Reefer().CalculateDeliveryCost(route, _cargo));
    }

    [Fact]
    public void PlaneCostAddsSurchargePerKg()
    {
        var route = TestData.ShortRoute;
        decimal expected = (decimal)route.DistanceKm * 120m + 100 * CargoPlane.PerKgSurcharge;

        Assert.Equal(expected, TestData.Plane().CalculateDeliveryCost(route, _cargo));
    }

    [Fact]
    public void ShipCostAddsPortFeeAndOversizedSurcharge()
    {
        var route = TestData.ShortRoute;
        var cargo = new Cargo[] { TestData.Standard(), TestData.Oversized(), TestData.Oversized() };
        decimal expected = (decimal)route.DistanceKm * 8m + CargoShip.PortFee + 2 * CargoShip.OversizedSurcharge;

        Assert.Equal(expected, TestData.Ship().CalculateDeliveryCost(route, cargo));
    }

    [Fact]
    public void DroneCostAddsLaunchFeeAndRefusesLongRoute()
    {
        var drone = TestData.Drone();
        var parcel = new Cargo[] { TestData.Standard(1) };

        Assert.Equal(DroneCourier.LaunchFee, drone.CalculateDeliveryCost(TestData.ZeroRoute, parcel));
        Assert.False(drone.CanServe(TestData.ShortRoute));
        Assert.Throws<RouteNotFoundException>(() => drone.CalculateDeliveryCost(TestData.ShortRoute, parcel));
    }

    [Fact]
    public void ZeroDistanceLeavesOnlyFixedParts()
    {
        var route = TestData.ZeroRoute;

        Assert.Equal(0, route.DistanceKm);
        Assert.Equal(0m, TestData.Truck().CalculateDeliveryCost(route, _cargo));
        Assert.Equal(100 * CargoPlane.PerKgSurcharge, TestData.Plane().CalculateDeliveryCost(route, _cargo));
        Assert.Equal(CargoShip.PortFee, TestData.Ship().CalculateDeliveryCost(route, _cargo));
    }

    [Fact]
    public void CanCarryAllowsExactLimitAndRefusesAboveIt()
    {
        var truck = TestData.Truck(maxLoadKg: 1000);

        Assert.True(truck.CanCarry(TestData.Standard(1000)));
        Assert.False(truck.CanCarry(TestData.Standard(1000.1)));
    }

    [Fact]
    public void InheritorsExtendBaseCanCarry()
    {
        Assert.False(TestData.Plane().CanCarry(TestData.Dangerous(hazardClass: 3)));
        Assert.True(TestData.Plane().CanCarry(TestData.Dangerous(hazardClass: 8)));
        Assert.False(TestData.Reefer().CanCarry(TestData.Perishable(temperatureC: -40)));
        Assert.True(TestData.Reefer().CanCarry(TestData.Perishable(temperatureC: -18)));
    }

    [Theory]
    [InlineData("tr-0001")]
    [InlineData("TR0001")]
    [InlineData("TRK-001")]
    public void InvalidRegistrationNumberThrows(string number)
    {
        Assert.Throws<ArgumentException>(() => new Truck(number, 1000, 10, 80, 30m));
    }

    [Fact]
    public void VehiclesAreComparedById()
    {
        var id = Guid.NewGuid();
        var first = new Truck("TR-0001", 1000, 10, 80, 30m, id);
        var second = new Truck("TR-0002", 2000, 20, 90, 25m, id);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(TestData.Truck(), TestData.Truck());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 0)]
    public void CargoWeightAndVolumeMustBePositive(double weight, double volume)
    {
        Assert.Throws<CargoValidationException>(() => new StandardCargo("Груз", weight, volume, 100m));
    }

    [Fact]
    public void RoutePointMinusOperatorGivesDistance()
    {
        var a = new RoutePoint("A", 0, 0);
        var b = new RoutePoint("B", 0, 1);

        Assert.InRange(b - a, 111.1, 111.3);
        Assert.Equal(0, a - a);
        Assert.StartsWith("A (", (string)a);
    }
}

public class RepositoryTests
{
    [Fact]
    public void AddThenIndexerReturnsItem()
    {
        var repository = new Repository<Vehicle>();
        var truck = TestData.Truck();

        repository.Add(truck);

        Assert.Equal(1, repository.Count);
        Assert.Same(truck, repository[truck.Id]);
        Assert.Throws<KeyNotFoundException>(() => repository[Guid.NewGuid()]);
    }

    [Fact]
    public void RemoveDeletesOnlyThatItem()
    {
        var repository = new Repository<Vehicle>();
        var truck = TestData.Truck();
        var ship = TestData.Ship();
        repository.Add(truck);
        repository.Add(ship);

        Assert.True(repository.Remove(truck));
        Assert.False(repository.Remove(truck));
        Assert.Equal(new Vehicle[] { ship }, repository);
    }

    [Fact]
    public void FindAllReturnsMatchingItems()
    {
        var repository = new Repository<Vehicle>();
        repository.Add(TestData.Truck());
        repository.Add(TestData.Plane());
        repository.Add(TestData.Ship());

        var big = repository.FindAll(v => v.MaxLoadKg > 20000);

        Assert.Equal(2, big.Count);
        Assert.DoesNotContain(big, v => v is Truck);
    }

    [Fact]
    public void IterationKeepsInsertionOrder()
    {
        var repository = new Repository<Vehicle>();
        var vehicles = new Vehicle[] { TestData.Ship(), TestData.Truck(), TestData.Drone() };
        foreach (var vehicle in vehicles)
        {
            repository.Add(vehicle);
        }

        var iterated = new List<Vehicle>();
        foreach (var vehicle in repository)
        {
            iterated.Add(vehicle);
        }

        Assert.Equal(vehicles, iterated);
    }

    [Fact]
    public void ReadOnlyRepositoryIsCovariant()
    {
        var trucks = new Repository<Truck>();
        trucks.Add(TestData.Truck());

        IReadOnlyRepository<Vehicle> vehicles = trucks;

        Assert.Equal(1, vehicles.Count);
        Assert.IsType<Truck>(vehicles.GetAll().Single());
    }

    [Fact]
    public void ToReportTableContainsHeadersAndValues()
    {
        string table = new[] { TestData.Truck() }.ToReportTable(
            ("Номер", v => v.RegistrationNumber),
            ("Тип", v => v.GetType().Name));

        Assert.Contains("Номер", table);
        Assert.Contains("TR-0001", table);
        Assert.Contains("Truck", table);
    }
}
