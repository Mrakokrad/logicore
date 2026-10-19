namespace LogiCore.Domain;

public class LogisticsException : Exception
{
    public LogisticsException(string message) : base(message) { }

    public LogisticsException(string message, Exception inner) : base(message, inner) { }
}

public class CargoValidationException : LogisticsException
{
    public CargoValidationException(string message) : base(message) { }
}

public class IncompatibleCargoException : LogisticsException
{
    public IncompatibleCargoException(string message) : base(message) { }
}

public class VehicleOverloadException : LogisticsException
{
    public VehicleOverloadException(string message) : base(message) { }
}

public class RouteNotFoundException : LogisticsException
{
    public RouteNotFoundException(string message) : base(message) { }
}

public class StateStorageException : LogisticsException
{
    public StateStorageException(string message, Exception inner) : base(message, inner) { }
}

public class InvalidOrderStateException : LogisticsException
{
    public InvalidOrderStateException(Guid orderId, OrderStatus status, string action)
        : base($"Операция {action} недопустима для заказа {orderId} в статусе {status}.")
    {
        CurrentStatus = status;
    }

    public OrderStatus CurrentStatus { get; }
}
