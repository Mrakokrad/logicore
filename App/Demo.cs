using System.Text;
using LogiCore.Domain;

namespace LogiCore.App;

public static class Cities
{
    private static readonly Dictionary<string, RoutePoint> Points = new()
    {
        ["Москва"] = new RoutePoint("Москва", 55.7558, 37.6173),
        ["Санкт-Петербург"] = new RoutePoint("Санкт-Петербург", 59.9343, 30.3351),
        ["Казань"] = new RoutePoint("Казань", 55.7963, 49.1088),
        ["Новосибирск"] = new RoutePoint("Новосибирск", 55.0084, 82.9357),
        ["Владивосток"] = new RoutePoint("Владивосток", 43.1155, 131.8855),
        ["Химки"] = new RoutePoint("Химки", 55.8970, 37.4297)
    };

    public static IReadOnlyList<string> Names => Points.Keys.ToList();

    public static Route Route(params string[] names)
    {
        var points = new List<RoutePoint>();
        foreach (string name in names)
        {
            if (!Points.TryGetValue(name, out var point))
            {
                throw new RouteNotFoundException($"Пункт «{name}» не найден.");
            }

            points.Add(point);
        }

        return new Route(points);
    }
}

public static class ReportPrinter
{
    public static string Build(ReportService reports)
    {
        var text = new StringBuilder();

        text.AppendLine("1) Топ-3 ТС по выручке");
        text.AppendLine(reports.TopVehiclesByRevenue(3).ToReportTable(
            ("ТС", r => r.Number),
            ("Тип", r => r.Type),
            ("Доставок", r => r.Deliveries.ToString()),
            ("Выручка", r => r.Revenue.ToString("N2"))));

        text.AppendLine("2) Заказы по статусам");
        text.AppendLine(reports.OrdersByStatus().ToReportTable(
            ("Статус", r => r.Status.ToString()),
            ("Кол-во", r => r.Count.ToString()),
            ("Сумма", r => r.TotalCost.ToString("N2"))));

        text.AppendLine("3) Средняя загрузка ТС по типам, %");
        text.AppendLine(reports.AverageLoadByVehicleType().ToReportTable(
            ("Тип ТС", r => r.Type),
            ("Заказов", r => r.Orders.ToString()),
            ("Загрузка, %", r => r.AverageLoadPercent.ToString("F1"))));

        text.AppendLine("4) Клиенты с суммой заказов больше 50 000 руб.");
        text.AppendLine(reports.CustomersAbove(50000m).ToReportTable(
            ("Клиент", r => r.Customer),
            ("Заказов", r => r.Orders.ToString()),
            ("Сумма", r => r.Total.ToString("N2"))));

        text.AppendLine("5) Груз -> заказ -> клиент");
        text.AppendLine(reports.CargoOrderCustomer().ToReportTable(
            ("Груз", r => r.Cargo),
            ("Заказ", r => r.OrderId),
            ("Статус", r => r.Status.ToString()),
            ("Клиент", r => r.Customer)));

        text.AppendLine("6) Класс опасности -> количество грузов");
        text.AppendLine(reports.DangerousByHazardClass().ToReportTable(
            ("Класс", p => p.Key.ToString()),
            ("Грузов", p => p.Value.ToString())));

        return text.ToString();
    }
}

public class Demo
{
    private readonly App _app;
    private readonly Dictionary<string, Cargo> _cargo = new();
    private readonly List<Customer> _customers = new();
    private readonly List<Order> _orders = new();

    public Demo(App app) => _app = app;

    public void Run()
    {
        _app.Logger.Reset();
        _app.Notifier.Subscribe(_app.Service);
        _app.Logger.Subscribe(_app.Service);

        CreateFleetAndCargo();
        ShowLanguageFeatures();
        PlaceOrders();
        TryOverload();
        AssignAndCalculate();
        RunLifecycle();
        Header("6. LINQ-отчёты");
        Console.WriteLine(ReportPrinter.Build(_app.Reports));
        SaveClearLoad();
    }

    private static void Header(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 60));
        Console.WriteLine(title);
        Console.WriteLine(new string('=', 60));
    }

    private void CreateFleetAndCargo()
    {
        Header("1. Фабрика создаёт парк и грузы");

        var fleetSpecs = new[]
        {
            new VehicleSpec("truck", "TR-0001", 20000, 82, 80, 30m),
            new VehicleSpec("truck", "TR-0002", 8000, 40, 90, 25m),
            new VehicleSpec("refrigerator", "RF-0001", 12000, 50, 75, 40m)
                { MinTemperatureC = -25, MaxTemperatureC = 8 },
            new VehicleSpec("plane", "AV-0001", 40000, 300, 800, 120m),
            new VehicleSpec("ship", "SH-0001", 500000, 5000, 35, 8m),
            new VehicleSpec("drone", "DR-0001", 5, 0.05, 60, 15m) { MaxRangeKm = 30 }
        };
        foreach (var spec in fleetSpecs)
        {
            var vehicle = _app.VehicleFactory.Create(spec);
            _app.State.Fleet.Add(vehicle);
            Console.WriteLine($"  {vehicle} [{vehicle.Conditions}]");
        }

        Console.WriteLine();
        var cargoSpecs = new[]
        {
            new CargoSpec("standard", "Мебель", 1500, 12, 300000m),
            new CargoSpec("standard", "Книги", 800, 3, 120000m),
            new CargoSpec("standard", "Документы", 1.2, 0.01, 5000m),
            new CargoSpec("perishable", "Молоко", 2000, 6, 150000m)
                { ExpirationDate = DateTime.Today.AddDays(7), RequiredTemperatureC = 4 },
            new CargoSpec("perishable", "Мороженое", 1000, 4, 200000m)
                { ExpirationDate = DateTime.Today.AddDays(30), RequiredTemperatureC = -18 },
            new CargoSpec("perishable", "Йогурт", 300, 1, 30000m)
                { ExpirationDate = DateTime.Today.AddDays(5), RequiredTemperatureC = 4 },
            new CargoSpec("fragile", "Стекло", 400, 2, 250000m) { RiskFactor = 2.5 },
            new CargoSpec("fragile", "Керамика", 250, 1.5, 180000m) { RiskFactor = 1.5 },
            new CargoSpec("dangerous", "Краска", 600, 1, 90000m) { HazardClass = 3 },
            new CargoSpec("dangerous", "Кислота", 500, 0.8, 70000m) { HazardClass = 8 },
            new CargoSpec("oversized", "Турбина", 25000, 120, 5000000m) { LengthM = 18 }
        };
        foreach (var spec in cargoSpecs)
        {
            var cargo = _app.CargoFactory.Create(spec);
            _cargo[cargo.Description] = cargo;
            Console.WriteLine($"  {cargo}");
        }

        foreach (string name in new[] { "ООО «Мебельный двор»", "ИП Свежесть", "Галерея «Витраж»", "АО «Техмаш»", "Курьер Иванов" })
        {
            var customer = new Customer(name, "mail@example.com");
            _customers.Add(customer);
            _app.State.Customers.Add(customer);
        }

        Console.WriteLine($"\n  ТС: {_app.State.Fleet.Count}, грузов: {_cargo.Count}, клиентов: {_customers.Count}");
    }

    private void ShowLanguageFeatures()
    {
        Header("Флаги, вариантность, явная реализация интерфейса, Repository");

        var required = TransportConditions.Refrigerated | TransportConditions.Sealed;
        var matching = _app.State.Fleet.FindAll(v => (v.Conditions & required) == required);
        Console.WriteLine($"  Нужны {required}: {string.Join(", ", matching.Select(v => v.RegistrationNumber))}");

        var conditions = TransportConditions.Sealed | TransportConditions.LongRange;
        Console.WriteLine($"  Sealed | LongRange = {conditions} ({(int)conditions})");
        conditions &= ~TransportConditions.Sealed;
        Console.WriteLine($"  после & ~Sealed = {conditions}, HasFlag(LongRange) = {conditions.HasFlag(TransportConditions.LongRange)}");

        var trucks = new Repository<Truck>();
        foreach (var truck in _app.State.Fleet.OfType<Truck>())
        {
            trucks.Add(truck);
        }

        IReadOnlyRepository<Vehicle> covariant = trucks;
        Console.WriteLine($"  Ковариантность: IReadOnlyRepository<Vehicle> = Repository<Truck>, элементов {covariant.Count}");

        IValidator<PerishableCargo> contravariant = _app.ExpirationValidator;
        var oldKefir = new PerishableCargo("Кефир", 50, 0.2, 3000m, DateTime.Today.AddDays(-1), 4);
        Console.WriteLine($"  Контравариантность: IValidator<PerishableCargo> = валидатор Cargo -> {contravariant.Validate(oldKefir)}");

        IInsurable glass = (FragileCargo)_cargo["Стекло"];
        Console.WriteLine($"  Явная реализация: ((IInsurable)стекло).InsuranceRate = {glass.InsuranceRate:P1}");

        var route = Cities.Route("Москва", "Санкт-Петербург");
        Console.WriteLine($"  RoutePoint: {(string)route.Points[0]} - {(string)route.Points[1]} = {route.Points[1] - route.Points[0]:F0} км");
    }

    private void PlaceOrders()
    {
        Header("2. Оформление заказов");

        _orders.Add(_app.Service.PlaceOrder(_customers[0], new[] { _cargo["Мебель"], _cargo["Книги"] },
            Cities.Route("Москва", "Санкт-Петербург")));
        _orders.Add(_app.Service.PlaceOrder(_customers[1], new[] { _cargo["Молоко"], _cargo["Мороженое"] },
            Cities.Route("Москва", "Казань")));
        _orders.Add(_app.Service.PlaceOrder(_customers[2], new[] { _cargo["Стекло"], _cargo["Керамика"] },
            Cities.Route("Москва", "Новосибирск"),
            ExtraServices.Insurance | ExtraServices.FragilePackaging | ExtraServices.Urgent));
        _orders.Add(_app.Service.PlaceOrder(_customers[3], new[] { _cargo["Турбина"], _cargo["Кислота"] },
            Cities.Route("Москва", "Владивосток"), ExtraServices.Insurance));
        _orders.Add(_app.Service.PlaceOrder(_customers[4], new[] { _cargo["Документы"] },
            Cities.Route("Москва", "Химки")));

        Console.WriteLine("\n  Заведомо невалидный заказ: краска (опасный) + йогурт (скоропортящийся)");
        try
        {
            _app.Service.PlaceOrder(_customers[1], new[] { _cargo["Краска"], _cargo["Йогурт"] },
                Cities.Route("Москва", "Казань"));
        }
        catch (IncompatibleCargoException ex)
        {
            Console.WriteLine($"  Перехвачено IncompatibleCargoException: {ex.Message}");
        }

        Console.WriteLine($"\n  Оформлено заказов: {_orders.Count}");
    }

    private void TryOverload()
    {
        Header("3. Попытка перегруза");

        var drone = _app.State.Fleet.OfType<DroneCourier>().First();
        Console.WriteLine($"  Отдаём заказ на {_orders[0].TotalWeightKg} кг дрону {drone.RegistrationNumber}");
        try
        {
            _app.Service.AssignVehicle(_orders[0], drone, _app.Tariffs[0]);
        }
        catch (VehicleOverloadException ex)
        {
            Console.WriteLine($"  Перехвачено VehicleOverloadException: {ex.Message}");
        }
    }

    private void AssignAndCalculate()
    {
        Header("4. Подбор ТС и расчёт стоимости");

        Console.WriteLine("  Подбираем самое дешёвое свободное ТС:");
        Assign(_orders[3], _app.Tariffs[2]);
        Assign(_orders[0], _app.Tariffs[0]);
        Assign(_orders[1], _app.Tariffs[0]);
        Assign(_orders[4], _app.Tariffs[0]);

        var glassOrder = _orders[2];
        Console.WriteLine($"\n  Заказ {glassOrder.ShortId}: {glassOrder.Route}, услуги: {glassOrder.Services}");
        Console.WriteLine("  Цена по каждому тарифу для подходящих ТС:");
        var columns = new List<(string, Func<Vehicle, string>)> { ("ТС", v => v.RegistrationNumber) };
        foreach (var tariff in _app.Tariffs)
        {
            columns.Add((tariff.Name, v => _app.Service.Quote(glassOrder, v, tariff).Total.ToString("N2")));
        }

        Console.WriteLine(_app.Service.FindSuitableVehicles(glassOrder).ToReportTable(columns.ToArray()));

        var cost = _app.Service.AssignCheapestVehicle(glassOrder, _app.Tariffs[1]);
        Console.WriteLine($"  Назначено {glassOrder.AssignedVehicle!.RegistrationNumber}, расчёт по шагам:");
        foreach (string line in cost!.Describe().Split(Environment.NewLine))
        {
            Console.WriteLine($"    {line}");
        }

        Console.WriteLine("\n  Порядок декораторов важен (база 10 000):");
        IDeliveryCost basic = new BaseDeliveryCost(10000m, "База");
        var settings = TariffSettings.Instance;
        var insuranceFirst = new UrgencyDecorator(
            new InsuranceDecorator(basic, glassOrder.Cargo, settings.DefaultInsuranceRate), settings.UrgencyFactor);
        var urgencyFirst = new InsuranceDecorator(
            new UrgencyDecorator(basic, settings.UrgencyFactor), glassOrder.Cargo, settings.DefaultInsuranceRate);
        Console.WriteLine($"    страховка -> срочность: {insuranceFirst.Total:N2}");
        Console.WriteLine($"    срочность -> страховка: {urgencyFirst.Total:N2}");
    }

    private void RunLifecycle()
    {
        Header("5. Жизненный цикл заказов и события");

        Console.WriteLine($"  Полный цикл заказа {_orders[2].ShortId}:");
        _app.Service.StartDelivery(_orders[2]);
        _app.Service.CompleteDelivery(_orders[2]);

        _app.Service.StartDelivery(_orders[0]);
        _app.Service.CompleteDelivery(_orders[0]);

        Console.WriteLine("\n  Отписываем ConsoleNotifier: события заказа молока уйдут только в журнал");
        _app.Notifier.Unsubscribe(_app.Service);
        try
        {
            _app.Service.StartDelivery(_orders[1]);
            _app.Service.CompleteDelivery(_orders[1]);
        }
        finally
        {
            _app.Notifier.Subscribe(_app.Service);
            Console.WriteLine("  ConsoleNotifier снова подписан\n");
        }

        _app.Service.StartDelivery(_orders[3]);
        _app.Service.CancelOrder(_orders[4]);

        try
        {
            _app.Service.StartDelivery(_orders[4]);
        }
        catch (InvalidOrderStateException ex) when (ex.CurrentStatus == OrderStatus.Cancelled)
        {
            Console.WriteLine($"  Отменённый заказ нельзя отправить: {ex.Message}");
        }

        Console.WriteLine($"\n  Выручка компании: {_app.Service.TotalRevenue:N2} руб.");
        Console.WriteLine($"\n  Журнал {_app.Logger.Path}:");
        Console.WriteLine(_app.Logger.ReadAll());
    }

    private void SaveClearLoad()
    {
        Header("7. Сохранение -> очистка -> загрузка");

        string before = ReportPrinter.Build(_app.Reports);
        _app.Storage.Save(_app.State, App.StateFile);
        Console.WriteLine($"  Сохранено в {Path.GetFullPath(App.StateFile)}");

        _app.State.Clear();
        Console.WriteLine($"  После очистки: ТС {_app.State.Fleet.Count}, клиентов {_app.State.Customers.Count}, " +
                          $"заказов {_app.State.Orders.Count}");

        _app.Storage.Load(App.StateFile, _app.State);
        Console.WriteLine($"  После загрузки: ТС {_app.State.Fleet.Count}, клиентов {_app.State.Customers.Count}, " +
                          $"заказов {_app.State.Orders.Count}");

        string after = ReportPrinter.Build(_app.Reports);
        Console.WriteLine();
        Console.WriteLine(after);
        Console.WriteLine(before == after
            ? "  Отчёты до сохранения и после загрузки совпадают."
            : "  Отчёты различаются!");

        Console.WriteLine($"\n  Несуществующий файл: Load вернул {_app.Storage.Load("no_such_file.json", _app.State)}");

        File.WriteAllText("broken.json", "{ \"Fleet\": [ { \"$type\": \"submarine\" } ] }");
        try
        {
            _app.Storage.Load("broken.json", _app.State);
        }
        catch (StateStorageException ex)
        {
            Console.WriteLine($"  Повреждённый файл: {ex.Message}");
        }
        finally
        {
            File.Delete("broken.json");
        }

        Console.WriteLine($"  Состояние не испорчено: заказов {_app.State.Orders.Count}");
    }

    private void Assign(Order order, ITariffStrategy tariff)
    {
        var cost = _app.Service.AssignCheapestVehicle(order, tariff);
        Console.WriteLine(cost is null
            ? $"  Для заказа {order.ShortId} нет подходящего свободного ТС"
            : $"  Заказ {order.ShortId} -> {order.AssignedVehicle!.RegistrationNumber}, {cost.Total:N2} руб., " +
              $"в пути ~{order.Route.EstimateTime(order.AssignedVehicle).TotalHours:F1} ч");
    }
}
