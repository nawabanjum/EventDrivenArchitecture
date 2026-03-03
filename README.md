# Event-Driven Architecture — Order Processing System

> **Day 11** of the 30-Day Solution Architect Roadmap
> **Topic:** Event-Driven Architecture with Azure Service Bus, RabbitMQ & .NET 9.0

## Overview

This project demonstrates an **event-driven microservices architecture** for an Order Processing System. Four independent Web APIs communicate asynchronously using the publish/subscribe pattern through two messaging brokers:

- **Azure Service Bus** — Cloud-managed enterprise broker (Topics & Subscriptions)
- **RabbitMQ** — Self-hosted open-source broker (Exchanges & Queues)

Both implementations use the **same domain events and models**, with separate API controllers for each broker, allowing side-by-side comparison.

When a customer places an order, events flow automatically through the system — triggering payment processing, inventory reservation, and customer notification — without any service calling another directly.

---

## Architecture

### Azure Service Bus Flow

```
POST /api/orders
        |
        v
   [OrderApi] ---publishes---> Topic: "order-placed"
   (port 5001)                        |
                          +-----------+-----------+
                          v                       v
                   [PaymentApi]            [InventoryApi]
                   (port 5002)             (port 5003)
                   payment-subscription    inventory-subscription
                          |
                          | publishes
                          v
                    Topic: "order-paid"
                          |
                          v
                  [NotificationApi]
                  (port 5004)
                  notification-subscription
```

### RabbitMQ Flow

```
POST /api/rabbitmq/orders
        |
        v
   [OrderApi] ---publishes---> Exchange: "order-placed-exchange" (fanout)
   (port 5001)                        |
                          +-----------+-----------+
                          v                       v
                   [PaymentApi]            [InventoryApi]
                   (port 5002)             (port 5003)
                   payment-order-          inventory-order-
                   placed-queue            placed-queue
                          |
                          | publishes
                          v
                    Exchange: "order-paid-exchange" (fanout)
                          |
                          v
                  [NotificationApi]
                  (port 5004)
                  notification-order-paid-queue
```

---

## Projects

| Project | Type | Port | Responsibility |
|---|---|---|---|
| `OrderProcessing.Domain` | Class Library | — | Shared events (`OrderPlacedEvent`, `OrderPaidEvent`) and models (`Order`) |
| `OrderProcessing.OrderApi` | Web API | 5001 | Accepts orders and publishes `OrderPlacedEvent` |
| `OrderProcessing.PaymentApi` | Web API | 5002 | Consumes `OrderPlacedEvent`, processes payment, publishes `OrderPaidEvent` |
| `OrderProcessing.InventoryApi` | Web API | 5003 | Consumes `OrderPlacedEvent`, reserves inventory stock |
| `OrderProcessing.NotificationApi` | Web API | 5004 | Consumes `OrderPaidEvent`, sends customer notification |

---

## API Endpoints

### Azure Service Bus Endpoints

| API | Method | Endpoint | Description |
|---|---|---|---|
| OrderApi | POST | `/api/orders` | Place order via Azure Service Bus |
| PaymentApi | GET | `/api/payments/{orderId}` | Get payment status |
| InventoryApi | GET | `/api/inventory/{orderId}` | Get reservation status |
| NotificationApi | GET | `/api/notifications/{orderId}` | Get notification status |

### RabbitMQ Endpoints

| API | Method | Endpoint | Description |
|---|---|---|---|
| OrderApi | POST | `/api/rabbitmq/orders` | Place order via RabbitMQ |
| PaymentApi | GET | `/api/rabbitmq/payments/{orderId}` | Get payment status |
| InventoryApi | GET | `/api/rabbitmq/inventory/{orderId}` | Get reservation status |
| NotificationApi | GET | `/api/rabbitmq/notifications/{orderId}` | Get notification status |

---

## Tech Stack

- **.NET 9.0** — Web API with Controllers
- **Azure Service Bus** (Standard tier) — Topics & Subscriptions for pub/sub messaging
- **RabbitMQ** — Exchanges & Queues for pub/sub messaging
- **Azure.Messaging.ServiceBus** — Official .NET SDK for Azure Service Bus
- **RabbitMQ.Client** (v7.2.1) — Official .NET client for RabbitMQ
- **BackgroundService** — Hosted services for consuming messages alongside HTTP endpoints
- **Docker** — For running RabbitMQ locally

---

## Azure Service Bus Setup

### Resources Required

| Resource | Name | Purpose |
|---|---|---|
| Namespace | *(your-namespace)* | Service Bus namespace (Standard tier) |
| Topic | `order-placed` | Published by OrderApi |
| Topic | `order-paid` | Published by PaymentApi |
| Subscription | `payment-subscription` | On `order-placed` topic — consumed by PaymentApi |
| Subscription | `inventory-subscription` | On `order-placed` topic — consumed by InventoryApi |
| Subscription | `notification-subscription` | On `order-paid` topic — consumed by NotificationApi |

### Setup Steps (Azure Portal)

1. Create an **Azure Service Bus namespace** (Standard tier — required for topics)
2. Create topic: `order-placed`
3. Create topic: `order-paid`
4. Under `order-placed` topic, create subscriptions: `payment-subscription`, `inventory-subscription`
5. Under `order-paid` topic, create subscription: `notification-subscription`
6. Go to **Shared access policies** → copy the **connection string**

---

## RabbitMQ Setup

### Run RabbitMQ via Docker

```bash
docker run -d --hostname rabbitmq --name rabbitmq \
  -p 5672:5672 -p 15672:15672 \
  rabbitmq:3-management
```

- **AMQP port:** `5672` (application connection)
- **Management UI:** `http://localhost:15672` (username: `guest`, password: `guest`)

### Resources (Auto-Created by the Application)

| Resource | Name | Type | Purpose |
|---|---|---|---|
| Exchange | `order-placed-exchange` | Fanout | Published by OrderApi |
| Exchange | `order-paid-exchange` | Fanout | Published by PaymentApi |
| Queue | `payment-order-placed-queue` | Durable | Bound to `order-placed-exchange` — consumed by PaymentApi |
| Queue | `inventory-order-placed-queue` | Durable | Bound to `order-placed-exchange` — consumed by InventoryApi |
| Queue | `notification-order-paid-queue` | Durable | Bound to `order-paid-exchange` — consumed by NotificationApi |

> **Note:** Unlike Azure Service Bus (where topics/subscriptions must be created manually), RabbitMQ exchanges, queues, and bindings are **declared automatically** by the application on startup.

---

## Getting Started

### Prerequisites

- .NET 9.0 SDK
- **For Azure Service Bus:** Azure subscription with Service Bus namespace configured
- **For RabbitMQ:** Docker installed (or RabbitMQ installed locally)

### Create the Solution

```bash
# Create solution
dotnet new sln -n EventDrivenArchitecture

# Create projects
mkdir src
dotnet new classlib -n OrderProcessing.Domain -o src/OrderProcessing.Domain -f net9.0
dotnet new webapi -n OrderProcessing.OrderApi -o src/OrderProcessing.OrderApi -f net9.0
dotnet new webapi -n OrderProcessing.PaymentApi -o src/OrderProcessing.PaymentApi -f net9.0
dotnet new webapi -n OrderProcessing.InventoryApi -o src/OrderProcessing.InventoryApi -f net9.0
dotnet new webapi -n OrderProcessing.NotificationApi -o src/OrderProcessing.NotificationApi -f net9.0

# Add projects to solution
dotnet sln add src/OrderProcessing.Domain
dotnet sln add src/OrderProcessing.OrderApi
dotnet sln add src/OrderProcessing.PaymentApi
dotnet sln add src/OrderProcessing.InventoryApi
dotnet sln add src/OrderProcessing.NotificationApi

# Add project references (all APIs reference Domain)
dotnet add src/OrderProcessing.OrderApi reference src/OrderProcessing.Domain
dotnet add src/OrderProcessing.PaymentApi reference src/OrderProcessing.Domain
dotnet add src/OrderProcessing.InventoryApi reference src/OrderProcessing.Domain
dotnet add src/OrderProcessing.NotificationApi reference src/OrderProcessing.Domain

# Add Azure Service Bus NuGet package
dotnet add src/OrderProcessing.OrderApi package Azure.Messaging.ServiceBus
dotnet add src/OrderProcessing.PaymentApi package Azure.Messaging.ServiceBus
dotnet add src/OrderProcessing.InventoryApi package Azure.Messaging.ServiceBus
dotnet add src/OrderProcessing.NotificationApi package Azure.Messaging.ServiceBus

# Add RabbitMQ NuGet package
dotnet add src/OrderProcessing.OrderApi package RabbitMQ.Client
dotnet add src/OrderProcessing.PaymentApi package RabbitMQ.Client
dotnet add src/OrderProcessing.InventoryApi package RabbitMQ.Client
dotnet add src/OrderProcessing.NotificationApi package RabbitMQ.Client
```

### Configuration

**Azure Service Bus** — Add to each API's `appsettings.json`:

```json
{
  "AzureServiceBus": {
    "ConnectionString": "<your-service-bus-connection-string>"
  }
}
```

**RabbitMQ** — Add to each API's `appsettings.json`:

```json
{
  "RabbitMQ": {
    "HostName": "localhost",
    "Port": 5672,
    "UserName": "guest",
    "Password": "guest",
    "OrderPlacedExchange": "order-placed-exchange",
    "OrderPaidExchange": "order-paid-exchange"
  }
}
```

> Consumer APIs also include their queue name (e.g., `"PaymentQueueName": "payment-order-placed-queue"`).

### Switching Between Brokers

In each API's `Program.cs`, you can toggle which broker is active by commenting/uncommenting the registration blocks:

```csharp
// --- Azure Service Bus Registration ---
// builder.Services.AddSingleton(new ServiceBusClient(...));
// builder.Services.AddHostedService<OrderPlacedConsumer>();

// --- RabbitMQ Registration ---
var rabbitConnection = await rabbitFactory.CreateConnectionAsync();
builder.Services.AddSingleton(rabbitConnection);
builder.Services.AddHostedService<RabbitMqOrderPlacedConsumer>();
```

Both can also run simultaneously — they use separate controllers and consumers.

### Run the Solution

Start all 4 APIs (in separate terminals or via Visual Studio multiple startup projects):

```bash
dotnet run --project src/OrderProcessing.OrderApi
dotnet run --project src/OrderProcessing.PaymentApi
dotnet run --project src/OrderProcessing.InventoryApi
dotnet run --project src/OrderProcessing.NotificationApi
```

---

## Testing the Event Flow

### Azure Service Bus

1. **Place an order:**
   ```bash
   curl -X POST http://localhost:5001/api/orders \
     -H "Content-Type: application/json" \
     -d '{"customerName": "John Doe", "product": "Laptop", "quantity": 1, "totalAmount": 999.99}'
   ```

2. **Verify via GET endpoints:**
   ```bash
   curl http://localhost:5002/api/payments/{orderId}
   curl http://localhost:5003/api/inventory/{orderId}
   curl http://localhost:5004/api/notifications/{orderId}
   ```

3. **Azure Portal** — Service Bus → Topics → check message counts and subscription activity

### RabbitMQ

1. **Place an order:**
   ```bash
   curl -X POST http://localhost:5001/api/rabbitmq/orders \
     -H "Content-Type: application/json" \
     -d '{"customerName": "Jane Smith", "product": "Keyboard", "quantity": 2, "totalAmount": 149.99}'
   ```

2. **Verify via GET endpoints:**
   ```bash
   curl http://localhost:5002/api/rabbitmq/payments/{orderId}
   curl http://localhost:5003/api/rabbitmq/inventory/{orderId}
   curl http://localhost:5004/api/rabbitmq/notifications/{orderId}
   ```

3. **RabbitMQ Management UI** — `http://localhost:15672` (guest/guest) → check exchanges, queues, and message rates

---

## Project Structure

```
src/
├── OrderProcessing.Domain/
│   ├── Events/
│   │   ├── OrderPlacedEvent.cs
│   │   └── OrderPaidEvent.cs
│   └── Models/
│       └── Order.cs
│
├── OrderProcessing.OrderApi/
│   ├── Controllers/
│   │   ├── OrdersController.cs              ← Azure Service Bus
│   │   └── RabbitMqOrdersController.cs      ← RabbitMQ
│   └── Services/
│       ├── ServiceBusPublisher.cs            ← Azure Service Bus
│       └── RabbitMqPublisher.cs              ← RabbitMQ
│
├── OrderProcessing.PaymentApi/
│   ├── Controllers/
│   │   ├── PaymentsController.cs             ← Azure Service Bus
│   │   └── RabbitMqPaymentsController.cs     ← RabbitMQ
│   ├── Consumers/
│   │   ├── OrderPlacedConsumer.cs            ← Azure Service Bus
│   │   └── RabbitMqOrderPlacedConsumer.cs    ← RabbitMQ
│   └── Services/
│       ├── ServiceBusPublisher.cs            ← Azure Service Bus
│       ├── RabbitMqPublisher.cs              ← RabbitMQ
│       └── PaymentStore.cs                   ← Shared
│
├── OrderProcessing.InventoryApi/
│   ├── Controllers/
│   │   ├── InventoryController.cs            ← Azure Service Bus
│   │   └── RabbitMqInventoryController.cs    ← RabbitMQ
│   ├── Consumers/
│   │   ├── OrderPlacedConsumer.cs            ← Azure Service Bus
│   │   └── RabbitMqOrderPlacedConsumer.cs    ← RabbitMQ
│   └── Services/
│       └── InventoryStore.cs                 ← Shared
│
└── OrderProcessing.NotificationApi/
    ├── Controllers/
    │   ├── NotificationsController.cs        ← Azure Service Bus
    │   └── RabbitMqNotificationsController.cs ← RabbitMQ
    ├── Consumers/
    │   ├── OrderPaidConsumer.cs              ← Azure Service Bus
    │   └── RabbitMqOrderPaidConsumer.cs       ← RabbitMQ
    └── Services/
        └── NotificationStore.cs              ← Shared
```

---

## Key Concepts Demonstrated

| Concept | How It's Used |
|---|---|
| **Event-Driven Architecture** | Services react to events instead of direct HTTP calls |
| **Publish/Subscribe** | One event (OrderPlaced) triggers multiple independent consumers |
| **Loose Coupling** | OrderApi doesn't know about Payment, Inventory, or Notification services |
| **Multiple Broker Support** | Same domain events flow through Azure Service Bus or RabbitMQ |
| **BackgroundService** | .NET hosted services consume messages while API serves HTTP requests |
| **Async Communication** | Services process events at their own pace — no blocking |

---

## Broker Comparison

| Feature | Azure Service Bus | RabbitMQ |
|---|---|---|
| **Type** | Cloud-managed (PaaS) | Self-hosted (open-source) |
| **Protocol** | AMQP (Azure SDK wrapper) | AMQP (native) |
| **Pub/Sub Model** | Topic + Subscription | Exchange + Queue + Binding |
| **Message Acknowledgment** | `CompleteMessageAsync()` | `BasicAckAsync()` |
| **Dead Letter Queue** | Built-in | Manual DLX configuration |
| **Duplicate Detection** | Built-in | Manual (via message deduplication plugins) |
| **Ordering** | Per-session FIFO | Per-queue FIFO |
| **Scaling** | Auto-managed by Azure | Manual (clustering, federation) |
| **Management UI** | Azure Portal | RabbitMQ Management Plugin (:15672) |
| **Cost** | Pay per operation | Free (self-hosted infrastructure cost) |
| **Best For** | Enterprise / cloud-native apps | On-premise / low-latency / full control |
| **.NET SDK** | `Azure.Messaging.ServiceBus` | `RabbitMQ.Client` |
| **Setup in this project** | Manual (Azure Portal) | Automatic (declared on app startup) |
