# Read-Your-Writes Demo (.NET + PostgreSQL + Redis)

A small demo project that shows how to implement **read-your-writes consistency**
in a typical backend stack:

- ASP.NET Core Minimal API
- PostgreSQL (leader + followers)
- Redis (to track last write per user)

> After a user updates their profile, they should **always** see the updated data
> on the next request — even if your reads normally go to lagging read replicas.

This repo demonstrates a practical pattern from *Designing Data-Intensive Applications*:
**read-your-writes consistency** on top of leader–follower replication.

---

## 🔧 Features

- Simple ASP.NET Core 8 minimal API
- PostgreSQL as the primary database
- Support for read replicas (followers) via separate connection strings
- Redis-based tracking of `last_write` per user
- Smart DB routing:
  - Writes → always to **leader**
  - Reads that must show the user’s own recent writes → **leader** for a short window
  - Other reads → **followers** (can be slightly stale)
- Swagger UI enabled in development

In this demo, both leader and follower connection strings point to the same Postgres
instance, but the code is ready for a real primary + replicas setup.

---

## 🏗 Architecture

Text diagram of the architecture:

```text
                       Internet
                           |
                           v
                   +---------------+
                   |    NGINX      |   (optional in local dev)
                   | api.example.com
                   +-------+-------+
                           |
                 -----------------------
                 |                     |
                 v                     v
        +----------------+    +----------------+
        | ASP.NET Core   |    | ASP.NET Core   |
        | API Instance 1 |    | API Instance 2 |
        +--------+-------+    +--------+-------+
                 |                     |
                 | (Npgsql / EF)       |
                 |                     |
     -------------+---------------------+--------------
     |                            |                   |
     v                            v                   v
+-----------+             +---------------+   +---------------+
| Postgres  |   WAL       | Postgres      |   | Postgres      |
| Primary   | ==========> | Replica #1    |...| Replica #N    |
| (Leader)  |  streaming  | (Follower)    |   | (Follower)    |
+-----------+             +---------------+   +---------------+

         ^
         |
         |  last_write per user
         v
   +--------------------+
   | Redis / Dist. Cache|
   | (user:last_write)  |
   +--------------------+
```

The important part: the **API layer** decides whether to talk to the leader or to a follower
for each request, based on a `last_write` marker per user.

---

## 🧠 How read-your-writes is implemented

We use a **time-based** strategy:

1. All **writes** go to the primary (leader) database.
2. After a successful write, we record in Redis:

   ```text
   Key: user:{userId}:last_write_utc
   Val: 2025-11-17T11:25:30Z (UTC timestamp)
   TTL: 10 minutes
   ```

3. For **read endpoints** that require read-your-writes (e.g. `GET /api/me`):
   - If the user has a recent `last_write_utc` (within X seconds, default 5s),
     we route the read to the **leader**.
   - Otherwise, we route it to a **follower**.

4. For **read endpoints** that are OK to be slightly stale (e.g. `GET /api/products`),
   we always route to **followers**.

In production, you could upgrade this to an **LSN-based** approach using
`pg_current_wal_lsn()` and `pg_last_wal_replay_lsn()`, but this repo keeps it
simple and time-based so you can focus on the concept.

---

## 📁 Project structure

```text
read-your-writes-demo/
├─ README.md
├─ docker-compose.yml
├─ src/
│  └─ ReadYourWritesDemo.Api/
│     ├─ ReadYourWritesDemo.Api.csproj
│     ├─ Program.cs
│     ├─ appsettings.json
│     ├─ Services/
│     │  ├─ IDbConnectionFactory.cs
│     │  ├─ DbConnectionFactory.cs
│     │  ├─ ILastWriteTracker.cs
│     │  ├─ LastWriteTracker.cs
│     │  ├─ DbRouter.cs
│     ├─ Models/
│     │  ├─ UserProfileDto.cs
│     │  ├─ ProductDto.cs
│     ├─ Infrastructure/
│     │  └─ DemoUserMiddleware.cs
│     └─ Endpoints/
│        └─ ApiEndpoints.cs
```

---

## 🚀 Running locally

### 1. Prerequisites

- .NET 8 SDK
- Docker + Docker Compose

### 2. Start Postgres + Redis

From the repo root:

```bash
docker-compose up -d
```

This starts:

- Postgres on `localhost:5432` (DB: `ryw_demo`, user: `postgres`, password: `postgres`)
- Redis on `localhost:6379`

### 3. Create demo tables & seed data

Connect to Postgres and run:

```sql
CREATE TABLE IF NOT EXISTS users (
    id         uuid PRIMARY KEY,
    email      text NOT NULL,
    name       text NOT NULL,
    avatar_url text NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS products (
    id    serial PRIMARY KEY,
    name  text NOT NULL,
    price numeric(12,2) NOT NULL
);

INSERT INTO products (name, price) VALUES
  ('Pro Subscription', 19.99),
  ('Enterprise Plan', 99.00),
  ('One-time Add-on', 9.99)
ON CONFLICT DO NOTHING;
```

### 4. Run the API

```bash
cd src/ReadYourWritesDemo.Api
dotnet run
```

The API will start on `http://localhost:5000` (or `https://localhost:7000`).

In Development, Swagger UI is available at:

- http://localhost:5000/swagger

---

## 🔐 Demo authentication (fake user)

To keep the example simple, there is no real auth. Instead:

- A middleware (`DemoUserMiddleware`) creates a fake user principal.
- You can optionally set the header `X-Demo-UserId` with a GUID to simulate different users.
- If the header is missing, a default demo user ID is used.

Example header:

```http
X-Demo-UserId: 11111111-1111-1111-1111-111111111111
```

The `DbRouter` uses the current user ID to read/write the `last_write` marker from Redis.

---

## 📡 API endpoints

Base path: `/api`

### 1. Get current user profile (read-your-writes safe)

```http
GET /api/me
```

- Reads the user profile.
- Uses **leader** if the user has a recent write (within the leader window).
- Otherwise, uses a follower.

Curl example:

```bash
curl -H "X-Demo-UserId: 11111111-1111-1111-1111-111111111111"          http://localhost:5000/api/me
```

### 2. Create/update current user profile (write)

```http
POST /api/me/profile
Content-Type: application/json
```

Body:

```json
{
  "email": "akbar@example.com",
  "name": "Akbar",
  "avatarUrl": "https://example.com/avatar.jpg"
}
```

- Always goes to the **leader**.
- Upserts into `users` table.
- Records `last_write` for this user in Redis.

Curl example:

```bash
curl -X POST http://localhost:5000/api/me/profile       -H "Content-Type: application/json"       -H "X-Demo-UserId: 11111111-1111-1111-1111-111111111111"       -d '{
    "email": "akbar@example.com",
    "name": "Akbar",
    "avatarUrl": "https://example.com/avatar.jpg"
  }'
```

### 3. Get product catalog (can be stale, uses followers)

```http
GET /api/products
```

- Always routed to **followers** (in this demo, same DB as leader).
- Read-only, safe to be slightly stale.

Curl example:

```bash
curl http://localhost:5000/api/products
```

---

## 🧩 Key components

- `DbConnectionFactory`  
  Creates Npgsql connections for **leader** and **follower** roles.

- `LastWriteTracker`  
  Stores and reads `last_write_utc` in Redis per user:
  - `RecordWriteAsync(userId)` is called after each successful write.
  - `GetLastWriteAsync(userId)` is used by `DbRouter` to decide where to read from.

- `DbRouter`  
  Core logic for read-your-writes routing:
  - `GetConnectionForWrite()` → always leader
  - `GetConnectionForReadAsync(bool requiresReadYourWrites)`  
    → leader or follower depending on user’s last write time

- `DemoUserMiddleware`  
  Injects a fake authenticated user based on `X-Demo-UserId` header.

- `ApiEndpoints`  
  Minimal API endpoints demonstrating:
  - read-your-writes-sensitive reads (`/api/me`)
  - writes (`/api/me/profile`)
  - regular reads (`/api/products`)



## 📝 License

Do whatever you want with this.  
Use it as a reference, a learning project, or a starting point for your own systems.

If you find it useful, a ⭐ on GitHub is always nice 🙂
