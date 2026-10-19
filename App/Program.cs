using System.Globalization;
using System.Text;
using LogiCore.Domain;

namespace LogiCore.App;

public class App
{
    public const string StateFile = "logicore_state.json";

    public App()
    {
        State = new CompanyState();
        ExpirationValidator = new CargoExpirationValidator(() => DateTime.Now);
        Service = new DeliveryService(State.Fleet, State.Orders,
            new CargoCompatibilityValidator(ExpirationValidator),
            new DeliveryCostCalculator(TariffSettings.Instance));
        Reports = new ReportService(State.Orders, State.Customers);
        Tariffs = new ITariffStrategy[] { new StandardTariff(), new ExpressTariff(), new HeavyCargoTariff() };
    }

    public CompanyState State { get; }

    public IValidator<Cargo> ExpirationValidator { get; }

    public DeliveryService Service { get; }

    public ReportService Reports { get; }

    public IReadOnlyList<ITariffStrategy> Tariffs { get; }

    public VehicleFactory VehicleFactory { get; } = new();

    public CargoFactory CargoFactory { get; } = new();

    public JsonStorage Storage { get; } = new();

    public ConsoleNotifier Notifier { get; } = new();

    public FileLogger Logger { get; } = new("delivery_log.txt");
}

public static class Program
{
    public static void Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");

        var app = new App();
        new Demo(app).Run();
        Menu(app);
    }

    private static void Menu(App app)
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("===== Меню =====");
            Console.WriteLine("  1. Создать заказ");
            Console.WriteLine("  2. Показать заказы");
            Console.WriteLine("  3. Начать доставку");
            Console.WriteLine("  4. Завершить доставку");
            Console.WriteLine("  5. Отменить заказ");
            Console.WriteLine("  6. Показать отчёты");
            Console.WriteLine("  7. Сохранить состояние");
            Console.WriteLine("  8. Загрузить состояние");
            Console.WriteLine("  0. Выход");
            Console.Write("Выбор: ");

            string? choice = Console.ReadLine();
            if (choice is null or "0")
            {
                return;
            }

            try
            {
                switch (choice)
                {
                    case "1": CreateOrder(app); break;
                    case "2": ShowOrders(app); break;
                    case "3": PickOrder(app, OrderStatus.Assigned, app.Service.StartDelivery); break;
                    case "4": PickOrder(app, OrderStatus.InTransit, app.Service.CompleteDelivery); break;
                    case "5": PickOrder(app, null, app.Service.CancelOrder); break;
                    case "6": Console.WriteLine(ReportPrinter.Build(app.Reports)); break;
                    case "7":
                        app.Storage.Save(app.State, App.StateFile);
                        Console.WriteLine("Сохранено.");
                        break;
                    case "8":
                        Console.WriteLine(app.Storage.Load(App.StateFile, app.State) ? "Загружено." : "Файла нет.");
                        break;
                    default: Console.WriteLine("Нет такого пункта."); break;
                }
            }
            catch (Exception ex) when (ex is LogisticsException or ArgumentException)
            {
                Console.WriteLine($"Ошибка: {ex.Message}");
            }
        }
    }

    private static void CreateOrder(App app)
    {
        var customers = app.State.Customers.ToList();
        int? customerIndex = Choose("Клиент", customers.Select(c => c.Name).ToList());
        var types = app.CargoFactory.KnownTypes.ToList();
        int? typeIndex = customerIndex is null ? null : Choose("Тип груза", types);
        if (customerIndex is null || typeIndex is null)
        {
            return;
        }

        Console.Write("Описание груза: ");
        string description = Console.ReadLine() ?? "Груз";
        double? weight = AskNumber("Вес, кг");
        double? volume = weight is null ? null : AskNumber("Объём, м³");
        if (weight is null || volume is null)
        {
            return;
        }

        var spec = new CargoSpec(types[typeIndex.Value], description, weight.Value, volume.Value, 10000m)
        {
            ExpirationDate = DateTime.Today.AddDays(30),
            RequiredTemperatureC = 4,
            RiskFactor = 2,
            HazardClass = 5,
            LengthM = 5
        };

        int? from = Choose("Откуда", Cities.Names);
        int? to = from is null ? null : Choose("Куда", Cities.Names);
        if (from is null || to is null)
        {
            return;
        }

        var route = Cities.Route(Cities.Names[from.Value], Cities.Names[to.Value]);
        var order = app.Service.PlaceOrder(customers[customerIndex.Value],
            new[] { app.CargoFactory.Create(spec) }, route);

        var cost = app.Service.AssignCheapestVehicle(order, app.Tariffs[0]);
        if (cost is null)
        {
            Console.WriteLine("Подходящего свободного ТС нет, заказ остался в статусе Created.");
            return;
        }

        Console.WriteLine($"Назначено ТС {order.AssignedVehicle!.RegistrationNumber}");
        Console.WriteLine(cost.Describe());
    }

    private static void ShowOrders(App app)
    {
        foreach (var order in app.State.Orders)
        {
            Console.WriteLine($"  {order}");
        }

        Console.WriteLine($"  Выручка: {app.Service.TotalRevenue:N2} руб.");
    }

    private static void PickOrder(App app, OrderStatus? status, Action<Order> action)
    {
        var orders = app.State.Orders.FindAll(o => status is null || o.Status == status).ToList();
        if (orders.Count == 0)
        {
            Console.WriteLine("Нет подходящих заказов.");
            return;
        }

        int? index = Choose("Заказ", orders.Select(o => o.ToString()!).ToList());
        if (index is not null)
        {
            action(orders[index.Value]);
            Console.WriteLine($"Готово: {orders[index.Value]}");
        }
    }

    private static int? Choose(string title, IReadOnlyList<string> options)
    {
        for (int i = 0; i < options.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {options[i]}");
        }

        Console.Write($"{title} (1-{options.Count}): ");
        string? input = Console.ReadLine();
        if (int.TryParse(input, out int value) && value >= 1 && value <= options.Count)
        {
            return value - 1;
        }

        Console.WriteLine("Нет такого варианта.");
        return null;
    }

    private static double? AskNumber(string title)
    {
        Console.Write($"{title}: ");
        string input = Console.ReadLine()?.Replace(',', '.') ?? string.Empty;
        if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return value;
        }

        Console.WriteLine("Это не число.");
        return null;
    }
}
