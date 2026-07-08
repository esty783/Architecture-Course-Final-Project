# 🏗 Mechira Microservices Platform

> A production-grade **microservices ecosystem** evolved from a monolithic e-commerce/auction API into a distributed, scalable platform demonstrating enterprise patterns: service isolation, asynchronous messaging, API gateway routing, intelligent caching, resilience patterns, and structured observability.

## 🎯 Mission: Monolith → Microservices

Transform a monolithic API into a production-ready microservices architecture:
- ✅ **Phase 1** → Database-per-service isolation *(Complete)*
- ✅ **Phase 2** → Correlation ID propagation & distributed tracing *(Complete)*
- ✅ **Phase 3** → API Gateway, BFF, load balancing *(Complete)*
- ✅ **Phase 4** → Async messaging, saga pattern, compensation *(Complete)*
- ✅ **Phase 5** → Centralized logging & observability *(Complete)*
- ⭐ **Bonus** → CI/CD pipeline with GitHub Actions *(Complete)*

## 🏛 System Architecture

```
┏━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┓
┃           🔌 API Gateway (Ocelot)                ┃
┃                Port 5000                         ┃
┗━━━━━━━━━━━━━━━━━━━┳━━━━━━━━━━━━━━━━━━━━━━━━━━━┛
                    ┃
       ┌────────────┼────────────┬────────────┐
       ┃            ┃            ┃            ┃
   ┌───▼────┐   ┌──▼────┐   ┌──▼────┐   ┌──▼────┐
   │  🔐    │   │📦    │   │📋   │   │🎲   │ 📬
   │ Auth   │   │Catalog│   │Orders│   │Lottery│Notif.
   │:5001   │   │:5002  │   │:5003 │   │:5004  │:5005
   └───┬────┘   └──┬────┘   └──┬────┘   └──┬────┘
       ┃          ┃           ┃            ┃
       └────┬─────┴─────┬─────┴────────────┘
            ┃           ┃
       ┌────▼──────┐ ┌─▼──────────┐
       │ 🐰 Message│ │ ⚡ Cache   │
       │ (Rabbit)  │ │ (Redis)    │
       └───────────┘ └────────────┘

💾 DATABASE-PER-SERVICE (SQL Server):
┏━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┓
┃     SQL Server Instance (Port 1433)     ┃
┣━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┫
┃ ◈ Mechira-AuthService                  ┃
┃ ◈ Mechira-CatalogService               ┃
┃ ◈ Mechira-OrderService                 ┃
┃ ◈ Mechira-LotteryService               ┃
┃ ◈ Mechira-NotificationService          ┃
┗━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┛
```

## 📡 Services & Endpoints

| Service | Port | Database | Purpose | Status |
|---------|:----:|----------|---------|:------:|
| **🔐 AuthService** | 5001 | `Mechira-AuthService` | User authentication, JWT tokens | ✅ |
| **📦 CatalogService** | 5002 | `Mechira-CatalogService` | Product catalog, donations (cached) | ✅ |
| **📋 OrderService** | 5003 | `Mechira-OrderService` | Order management, saga orchestration | ✅ |
| **🎲 LotteryService** | 5004 | `Mechira-LotteryService` | Lottery draw management | ✅ |
| **📬 NotificationService** | 5005 | `Mechira-NotificationService` | Event-driven email notifications | ✅ |
| **🔌 API Gateway** | 5000 | *None* | Routes requests, JWT validation | ✅ |
| **🐰 RabbitMQ** | 5672/15672 | N/A | Message broker for sagas | ✅ |
| **⚡ Redis** | 6379 | N/A | Distributed cache | ✅ |

## 🚀 Quick Start

### 📋 Prerequisites
- ✓ Docker & Docker Compose
- ✓ .NET 8.0 SDK (for local development)
- ✓ SQL Server Management Studio (optional, for DB inspection)

### 🐳 Run All Services via Docker
```bash
cd server
docker compose up -d

# Wait for healthy services (30-60 seconds)
docker compose ps

# View logs for any service
docker compose logs -f {service-name}
# Example: docker compose logs -f order-service
```

### 🌐 Access Services
- 🔌 **API Gateway:** http://localhost:5000/swagger
- 🔐 **AuthService:** http://localhost:5001/swagger
- 📦 **CatalogService:** http://localhost:5002/swagger
- 📋 **OrderService:** http://localhost:5003/swagger
- 🎲 **LotteryService:** http://localhost:5004/swagger
- 🐰 **RabbitMQ Management:** http://localhost:15672 (guest:guest)

### 💻 Run Locally (Development)

Each service can run independently:

```bash
# Terminal 1: AuthService
cd Services/AuthService && dotnet run

# Terminal 2: CatalogService
cd Services/CatalogService && dotnet run

# Terminal 3: OrderService
cd Services/OrderService && dotnet run

# Terminal 4: API Gateway
cd Gateway/ApiGateway && dotnet run

# All require RabbitMQ & Redis (see docker-compose.yml)
```

## 🔐 JWT Authentication

All services validate JWT tokens. Get a token from AuthService:

```bash
# 🔓 Login
curl -X POST http://localhost:5001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@example.com","password":"AdminPassword123!"}'

# 🎫 Use token in subsequent requests
curl -H "Authorization: Bearer {TOKEN}" \
  http://localhost:5000/api/orders
```

**Default user:** `admin@example.com` / `AdminPassword123!`

## 🐰 Event-Driven Messaging (RabbitMQ Saga)

Services communicate asynchronously for distributed transactions:

```
📋 OrderService publishes:
   └─→ OrderPlaced event

📦 CatalogService consumes:
   ├─→ Reserves inventory
   └─→ Publishes: InventoryReserved ✅ OR InventoryFailed ❌

📋 OrderService consumes:
   ├─→ InventoryReserved → Order confirmed ✅
   ├─→ InventoryFailed → Compensation (order cancelled) ↩️
   └─→ Publishes: OrderConfirmed or OrderCancelled

📬 NotificationService consumes:
   └─→ Sends email notifications 📧
```

## 🗄 Database Isolation (Phase 1 ✅)

**Architecture Pattern:** `database-per-service` — each microservice owns its database exclusively.

| Principle | Benefit |
|-----------|----------|
| 🔒 **Data Autonomy** | Services control their schema, deploy independently |
| 📈 **Scalability** | Each database optimized for its service's workload |
| 🛡 **Fault Isolation** | Database failure in one service won't crash others |
| 🔄 **Evolution** | Schema changes don't require coordination |
| ⚠️ **Tradeoff** | Multi-service transactions need saga pattern (RabbitMQ) |

See [ADR-001](./md/ADR_001_DATABASE_PER_SERVICE.md) for full architectural rationale.

### ✅ Verify Isolation
```bash
sqlcmd -S localhost,1433 -U sa -P YourPasswordHere123!
SELECT name FROM sys.databases WHERE name LIKE 'Mechira-%'
```

**Output:**
```
Mechira-AuthService
Mechira-CatalogService
Mechira-LotteryService
Mechira-NotificationService
Mechira-OrderService
```

## 🛡 Resilience Patterns (Production-Ready)

### ⚙️ Retry + Circuit Breaker (Polly)
OrderService → AuthService/CatalogService calls implement:

| Pattern | Config | Purpose |
|---------|--------|----------|
| **Retry** | 3 attempts, exponential backoff | Handle transient failures |
| **Circuit Breaker** | Opens after 50% failures, waits 10s | Prevent cascading failures |

```csharp
// Resilient HTTP call example
var policy = Policy
    .Handle<HttpRequestException>()
    .Or<TimeoutException>()
    .Retry(retryCount: 3)
    .Wrap(
        Policy
            .Handle<HttpRequestException>()
            .CircuitBreaker(
                handledEventsAllowedBeforeBreaking: 5, 
                durationOfBreak: TimeSpan.FromSeconds(10)
            )
    );
```

## 📊 Observability & Monitoring

### 📝 Structured Logging (Serilog)
All services stream rich logs:
- **Output:** Console (visible in `docker compose logs`)
- **Storage:** Files at `logs/{service-name}-{date}.txt` (daily rotation)
- **Format:** ISO 8601, log level, message, exception traces

### ❤️ Health Checks
Each service exposes `/api/health`:
```bash
curl http://localhost:5001/api/health
# { "status": "healthy", "timestamp": "2024-01-15T10:30:00Z", "database": "connected" }
```

### 🔗 Correlation IDs (Distributed Tracing)
Every request traced via `X-Correlation-ID`:
- 🔌 Generated at Gateway (or forwarded from client)
- 📤 Propagated to all downstream services via HTTP headers
- 📨 Propagated through RabbitMQ (MassTransit)
- 📝 Included in all Serilog entries
- 🔍 Aggregate at Seq: http://localhost:8081

## 🧪 Testing Strategy

### 🔬 Unit Tests
```bash
cd Services/AuthService/AuthService.Tests
dotnet test

# Run all service tests
dotnet test

# Generate coverage report
dotnet test /p:CollectCoverage=true
```

### 🐳 Integration Tests (Docker)
```bash
# Start ecosystem
docker compose up -d

# Health check all services
for svc in 5000 5001 5002 5003 5004; do
  curl http://localhost:$svc/api/health
done

# Test full saga (place order)
curl -X POST http://localhost:5000/api/orders \
  -H "Authorization: Bearer {TOKEN}" \
  -d '{"giftId": 1, "userId": 1}'
```

## 🗂 Project Structure

```
microservices/
├── 🔌 Gateway/
│   └── ApiGateway/              # Ocelot routing, JWT validation
│       ├── ocelot.json
│       ├── Middleware/
│       └── Program.cs
├── ⚙️ Services/
│   ├── 🔐 AuthService/          # User auth, JWT tokens
│   │   ├── Data/AuthDbContext.cs
│   │   └── Controllers/
│   ├── 📦 CatalogService/       # Product catalog (Redis-cached)
│   │   ├── Data/CatalogDbContext.cs
│   │   └── Repository/
│   ├── 📋 OrderService/         # Saga orchestration
│   │   ├── Data/OrderDbContext.cs
│   │   ├── Consumers/           # RabbitMQ consumers
│   │   └── Services/
│   ├── 🎲 LotteryService/       # Lottery management
│   │   └── Data/LotteryDbContext.cs
│   └── 📬 NotificationService/  # Event-driven email
│       └── Consumers/
├── 🔗 Shared/
│   └── SharedModels/            # DTOs, events, interfaces
│       ├── Dtos/
│       ├── Events/
│       └── Models/
├── 📖 md/
│   ├── ADR_001_DATABASE_PER_SERVICE.md
│   ├── ADR_002_SQL_SERVER_ALL_SERVICES.md
│   ├── PHASE1_DATABASE_ISOLATION.md
│   └── PHASE4_MESSAGING_SAGA.md
└── 🐳 docker-compose.yml       # Full stack orchestration
```

## 📋 Architecture Decision Records

**All design decisions documented in `md/` folder:**

| ADR | Title | Decision |
|:---:|-------|:--------:|
| **001** | [Database-Per-Service Pattern](./md/ADR_001_DATABASE_PER_SERVICE.md) | ✅ ACCEPTED |
| **002** | [SQL Server For All Services](./md/ADR_002_SQL_SERVER_ALL_SERVICES.md) | ✅ ACCEPTED |
| **003** | [RabbitMQ for Messaging](./md/ADR_003_MESSAGING_RABBITMQ.md) | ⏳ WIP |
| **004** | [Loki for Log Aggregation](./md/ADR_004_LOKI_LOGS.md) | ⏳ WIP |
| **005** | [NGINX for Load Balancing](./md/ADR_005_NGINX_LB.md) | ⏳ WIP |

## 🔧 Troubleshooting

### ⚠️ Services won't start
```bash
# Check database health
docker compose logs mssql-db | head -20

# Verify SQL Server responds
docker compose exec mssql-db /opt/mssql-tools/bin/sqlcmd -U sa -P YourPasswordHere123! -Q "SELECT 1"

# Check service logs
docker compose logs auth-service
```

### ⚠️ RabbitMQ connection errors
```bash
# Check RabbitMQ is up
docker compose logs rabbitmq-service

# Visit management console
# http://localhost:15672 (user: guest, pass: guest)
```

### ⚠️ Redis connection errors
```bash
# Check Redis is running
docker compose logs redis-cache

# Test Redis connection
docker compose exec redis-cache redis-cli ping
```

## ⚡ CI/CD Pipeline

GitHub Actions at `.github/workflows/ci.yml`:

| Job | Trigger | Action |
|-----|---------|--------|
| **Build & Test** | `push` + `pull_request` | `dotnet build` → `dotnet test` (blocks on failure) |
| **Docker Build** | `push` only (post-test) | Build & push 6 images to GHCR (tagged by commit SHA) |

## 📚 Documentation

- 📖 [Architecture Document](./md/ARCHITECTURE_DOCUMENT.md) — Diagrams, ADRs, technology choices
- 📊 [Phase 1: Monolith → Microservices](./md/PHASE1_MONOLITH_BASELINE.md) — Before/after, scaling issues
- 🗄 [Phase 1: Database Isolation](./md/PHASE1_DATABASE_ISOLATION.md) — Service data autonomy
- 🐰 [Phase 4: Messaging & Saga](./md/PHASE4_MESSAGING_SAGA.md) — Event-driven order processing
- ✅ [Demo Evidence](./md/DEMO_EVIDENCE.md) — Saga paths, cache hits, correlation traces
- 🔌 [API Gateway Config](./Gateway/ApiGateway/README.md) — Routing rules, middleware

## 📜 License

**Private project** — Educational purposes only.

---

## 📧 Questions?

Refer to ADRs and phase documentation for architecture deep-dives.
