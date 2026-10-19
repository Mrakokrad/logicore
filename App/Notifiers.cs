using LogiCore.Domain;

namespace LogiCore.App;

public class ConsoleNotifier
{
    public void Subscribe(IDeliveryEvents events)
    {
        events.OrderCreated += OnCreated;
        events.OrderStatusChanged += OnStatusChanged;
        events.VehicleOverloadAttempt += OnOverload;
        events.DeliveryCompleted += OnCompleted;
    }

    public void Unsubscribe(IDeliveryEvents events)
    {
        events.OrderCreated -= OnCreated;
        events.OrderStatusChanged -= OnStatusChanged;
        events.VehicleOverloadAttempt -= OnOverload;
        events.DeliveryCompleted -= OnCompleted;
    }

    private void OnCreated(object? sender, OrderCreatedEventArgs e) =>
        Console.WriteLine($"  [событие] Создан заказ {e.Order.ShortId} для {e.Order.Customer.Name}");

    private void OnStatusChanged(object? sender, OrderStatusChangedEventArgs e) =>
        Console.WriteLine($"  [событие] Заказ {e.Order.ShortId}: {e.OldStatus} -> {e.NewStatus}");

    private void OnOverload(object? sender, VehicleOverloadEventArgs e) =>
        Console.WriteLine($"  [событие] ПЕРЕГРУЗ {e.Vehicle.RegistrationNumber}: " +
                          $"запрошено {e.RequestedKg} кг / {e.RequestedM3} м³, " +
                          $"лимит {e.Vehicle.MaxLoadKg} кг / {e.Vehicle.MaxVolumeM3} м³");

    private void OnCompleted(object? sender, DeliveryCompletedEventArgs e) =>
        Console.WriteLine($"  [событие] Доставлен заказ {e.Order.ShortId} на {e.Order.TotalCost:N2} руб. " +
                          $"Выручка: {e.TotalRevenue:N2}");
}

public class FileLogger
{
    public FileLogger(string path) => Path = path;

    public string Path { get; }

    public void Subscribe(IDeliveryEvents events)
    {
        events.OrderCreated += OnCreated;
        events.OrderStatusChanged += OnStatusChanged;
        events.VehicleOverloadAttempt += OnOverload;
        events.DeliveryCompleted += OnCompleted;
    }

    public void Unsubscribe(IDeliveryEvents events)
    {
        events.OrderCreated -= OnCreated;
        events.OrderStatusChanged -= OnStatusChanged;
        events.VehicleOverloadAttempt -= OnOverload;
        events.DeliveryCompleted -= OnCompleted;
    }

    public void Reset() => File.Delete(Path);

    public string ReadAll() => File.Exists(Path) ? File.ReadAllText(Path) : string.Empty;

    private void OnCreated(object? sender, OrderCreatedEventArgs e) =>
        Write($"CREATED   {e.Order.ShortId} {e.Order.Customer.Name}");

    private void OnStatusChanged(object? sender, OrderStatusChangedEventArgs e) =>
        Write($"STATUS    {e.Order.ShortId} {e.OldStatus} -> {e.NewStatus}");

    private void OnOverload(object? sender, VehicleOverloadEventArgs e) =>
        Write($"OVERLOAD  {e.Vehicle.RegistrationNumber} {e.RequestedKg} кг");

    private void OnCompleted(object? sender, DeliveryCompletedEventArgs e) =>
        Write($"DELIVERED {e.Order.ShortId} {e.Order.TotalCost:F2}");

    private void Write(string line)
    {
        try
        {
            using var writer = new StreamWriter(Path, append: true);
            writer.WriteLine($"{DateTime.Now:HH:mm:ss} {line}");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"  Журнал недоступен: {ex.Message}");
        }
    }
}
