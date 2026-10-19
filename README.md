# LogiCore

Ядро информационной системы транспортно-логистической компании: приём заказов, проверка совместимости грузов, подбор транспорта, расчёт стоимости, статусы доставки, LINQ-отчёты и сохранение состояния в JSON.

C# / .NET 8, xUnit. 16 файлов с кодом, 50 тестов.

## Быстрый запуск

```bash
dotnet build
dotnet run --project LogiCore.App    # демо-сценарий, затем меню
dotnet test                          # 50 тестов
```

Демо проходит без ввода. Рядом появляются `delivery_log.txt` (журнал событий) и `logicore_state.json` (сохранённое состояние).

## Структура

```
LogiCore.sln
LogiCore.Domain/        class library — модель и бизнес-логика, не обращается к Console
    Vehicles.cs         Vehicle и 5 наследников, VehicleState, [Flags] TransportConditions
    Cargos.cs           Cargo и 5 наследников, интерфейсы грузов
    Orders.cs           struct RoutePoint, Route, Customer, Order (машина состояний)
    Repository.cs       IEntity, IReadOnlyRepository<out T>, Repository<T>, ToReportTable
    Validation.cs       IValidator<in T>, четыре правила совместимости грузов
    Pricing.cs          тарифы, декораторы услуг, TariffSettings, DeliveryCostCalculator
    Factories.cs        VehicleFactory, CargoFactory
    DeliveryService.cs  диспетчер: заказы, подбор ТС, статусы, события, выручка
    Reports.cs          шесть LINQ-отчётов
    JsonStorage.cs      сохранение и загрузка состояния
    Exceptions.cs       LogisticsException и наследники
LogiCore.App/           console app — Program.cs (меню), Demo.cs (сценарий), Notifiers.cs (подписчики)
LogiCore.Tests/         xUnit — VehicleTests.cs, ServiceTests.cs
docs/uml.png            диаграмма классов
```

## Как считается цена

Формула ТС (`CalculateDeliveryCost`) → тариф (`ITariffStrategy`) → цепочка декораторов услуг (страховка, срочность, упаковка). В демо каждый слой печатается отдельной строкой.

## Паттерны (Т7)

| Паттерн | Где | Зачем |
|---|---|---|
| Strategy | `ITariffStrategy`: Standard, Express, HeavyCargo | тариф выбирается во время работы, без `switch` в расчёте |
| Decorator | `IDeliveryCost` + три декоратора услуг | услуги комбинируются в любом наборе и порядке |
| Factory Method | `VehicleFactory`, `CargoFactory` | создание по описанию, новый тип добавляется через `Register` |
| Observer | события `DeliveryService`, подписчики `ConsoleNotifier` и `FileLogger` | диспетчер не знает, кто реагирует на события |
| Singleton | `TariffSettings.Instance` через `Lazy<T>` | одна конфигурация тарифов на приложение |

## Соответствие требованиям

| Требование | Где реализовано |
|---|---|
| 3.1 `Vehicle`: валидация номера, лимиты, `State`, абстрактный расчёт цены, пять наследников, `sealed DroneCourier` | `Vehicles.cs` |
| 3.2 `Cargo` и пять наследников, интерфейсы грузов, явная реализация `IInsurable` | `Cargos.cs` |
| 3.2 четыре правила совместимости в отдельном сервисе | `Validation.cs`, `CargoCompatibilityValidator` |
| 3.3 `Customer` с историей заказов, `Route`, `Order` как машина состояний | `Orders.cs` |
| 3.4 диспетчер: приём заказов, подбор самого дешёвого ТС, статусы, события, выручка | `DeliveryService.cs` |
| Т1 инкапсуляция: нет публичных полей, проверки в конструкторах и сеттерах, наружу `IReadOnlyCollection` | `Vehicles.cs`, `Cargos.cs`, `Orders.cs` |
| Т2 `abstract` / `virtual` / `override`, `sealed`, вызовы `base` | `Vehicles.cs` (`RefrigeratorTruck`, `DroneCourier`) |
| Т3 свои интерфейсы, явная реализация, ковариантность и контравариантность | `Repository.cs`, `Validation.cs`, `Cargos.cs` |
| Т4 `Repository<T> where T : class, IEntity`, индексатор, `yield return`, обобщённый метод расширения | `Repository.cs` |
| Т5 четыре события со своими `EventArgs`, свой делегат, два подписчика, отписка | `DeliveryService.cs`, `Notifiers.cs` |
| Т6 иерархия исключений, фильтры `when`, `using` и `finally` | `Exceptions.cs`, `JsonStorage.cs`, `Program.cs` |
| Т7 пять паттернов | см. таблицу выше |
| Т8 шесть LINQ-отчётов, один в синтаксисе запросов с `join` | `Reports.cs` |
| Т9 JSON через `System.Text.Json`, обработка отсутствующего и повреждённого файла | `JsonStorage.cs` |
| Т10 `[Flags]`-перечисления и структура с перегрузкой операторов | `Vehicles.cs`, `Orders.cs` (`RoutePoint`) |

Восстановление точных типов при загрузке JSON сделано атрибутами `[JsonDerivedType]` на `Vehicle` и `Cargo` (пункт Е3). Е1 и Е2 не выполнялись.

## UML-диаграмма

![UML](docs/uml.png)

## Тесты

| Класс | Что покрыто |
|---|---|
| `VehicleTests` | цена для каждого типа ТС, границы по весу, `base.CanCarry`, формат номера, равенство по Id, `RoutePoint` |
| `RepositoryTests` | добавление, удаление, поиск, итерация, индексатор, ковариантность |
| `ValidationTests` | все четыре правила совместимости, контравариантность |
| `OrderStateTests` | допустимые и недопустимые переходы статуса |
| `PricingTests` | цепочка декораторов и влияние порядка, тарифы, Singleton |
| `DeliveryServiceTests` | выбор самого дешёвого ТС, событие перегруза, жизненный цикл, отписка |
| `StorageTests` | сохранение и загрузка, отсутствующий и повреждённый файл |
