# Event-Driven Architecture — Order Processing System

> **Day 11** of the 30-Day Solution Architect Roadmap
> **Topic:** Event-Driven Architecture with Azure Service Bus & .NET 9.0

## Overview

This project demonstrates an **event-driven microservices architecture** for an Order Processing System. Four independent Web APIs communicate asynchronously through **Azure Service Bus Topics & Subscriptions** using the publish/subscribe pattern.

When a customer places an order, events flow automatically through the system — triggering payment processing, inventory reservation, and customer notification — without any service calling another directly.

## Architecture

```
Customer places order
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

## Projects

| Project | Type | Port | Responsibility |
|---|---|---|---|
| `OrderProcessing.Domain` | Class Library | — | Shared events (`OrderPlacedEvent`, `OrderPaidEvent`) and models (`Order`) |
| `OrderProcessing.OrderApi` | Web API | 5001 | Accepts orders via `POST /api/orders` and publishes `OrderPlacedEvent` |
| `OrderProcessing.PaymentApi` | Web API | 5002 | Consumes `OrderPlacedEvent`, processes payment, publishes `OrderPaidEvent` |
| `OrderProcessing.InventoryApi` | Web API | 5003 | Consumes `OrderPlacedEvent`, reserves inventory stock |
| `OrderProcessing.NotificationApi` | Web API | 5004 | Consumes `OrderPaidEvent`, sends customer notification |

## Tech Stack

- **.NET 9.0** — Web API with Minimal APIs / Controllers
- **Azure Service Bus** (Standard tier) — Topics & Subscriptions for pub/sub messaging
- **Azure.Messaging.ServiceBus** — Official .NET SDK for Azure Service Bus
- **BackgroundService** — Hosted services for consuming messages alongside HTTP endpoints

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

## Getting Started

### Prerequisites

- .NET 9.0 SDK
- Azure subscription with Service Bus namespace configured (see above)

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
```

### Configuration

Add to each API's `appsettings.json`:

```json
{
  "AzureServiceBus": {
    "ConnectionString": "<your-service-bus-connection-string>"
  }
}
```

### Run the Solution

Start all 4 APIs (in separate terminals or via Visual Studio multiple startup projects):

```bash
dotnet run --project src/OrderProcessing.OrderApi
dotnet run --project src/OrderProcessing.PaymentApi
dotnet run --project src/OrderProcessing.InventoryApi
dotnet run --project src/OrderProcessing.NotificationApi
```

## Testing the Event Flow

1. **POST** an order to OrderApi:
   ```bash
   curl -X POST http://localhost:5001/api/orders \
     -H "Content-Type: application/json" \
     -d '{"customerName": "John Doe", "product": "Laptop", "quantity": 1, "totalAmount": 999.99}'
   ```

2. **Check console logs** — each API logs when it receives and processes an event

3. **Verify via GET endpoints:**
   ```bash
   GET http://localhost:5002/api/payments/{orderId}
   GET http://localhost:5003/api/inventory/{orderId}
   GET http://localhost:5004/api/notifications/{orderId}
   ```

4. **Azure Portal** — Service Bus → Topics → check message counts and subscription activity

## Key Concepts Demonstrated

| Concept | How It's Used |
|---|---|
| **Event-Driven Architecture** | Services react to events instead of direct HTTP calls |
| **Publish/Subscribe** | One event (OrderPlaced) triggers multiple independent consumers |
| **Loose Coupling** | OrderApi doesn't know about Payment, Inventory, or Notification services |
| **Azure Service Bus Topics** | Fan-out messaging — one message delivered to all matching subscriptions |
| **BackgroundService** | .NET hosted services consume messages while API serves HTTP requests |
| **Async Communication** | Services process events at their own pace — no blocking |

## Azure Service Bus Key Features 

| Feature | Details |
|---|---|
| **Type** | Cloud-managed enterprise message broker |
| **Best For** | Enterprise messaging, ordering guarantees, transactional processing |
| **Pub/Sub** | Topics & Subscriptions |
| **Queuing** | Queues for point-to-point messaging |
| **Ordering** | Per-session message ordering (FIFO) |
| **Dead Letter Queue** | Built-in — failed messages automatically moved for inspection |
| **Duplicate Detection** | Built-in — prevents processing the same message twice |
| **Scaling** | Auto-managed by Azure |
| **Pricing** | Pay per operation (Standard tier required for Topics) |
| **.NET SDK** | `Azure.Messaging.ServiceBus` |
