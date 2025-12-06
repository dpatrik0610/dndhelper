# dndhelper backend

ASP.NET Core 8 API for running D&D campaigns with MongoDB persistence, JWT authentication, real-time SignalR updates, and a collection of domain services for characters, inventory, monsters, spells, notes, and campaigns.

## What it ships
- REST endpoints for auth (`/api/auth`), characters, equipment, inventory, monsters, spells, campaigns, sessions, notes, currency, and backups.
- JWT auth with role support (`User`, `Admin`) plus ownership-based authorization so only owners/admins can touch their resources.
- MongoDB data layer with a shared `MongoRepository<T>` (soft deletes, partial updates, and in-memory caching with admin controls at `/api/cache`).
- Real-time layer: SignalR hub at `/hubs/notifications` for targeted user notifications and entity change broadcasts (`EntityChanged`, `EntityBatchChanged`).
- Serilog logging to console and `Logs/dndhelper.log`, centralized exception handling middleware, health check at `/health`, and Swagger UI in Development.
- Backup/restore endpoints (`/api/backup/{collection}`) that wrap `mongodump`/`mongorestore` (Mongo Database Tools are installed in the Docker image).
- Optional D&D 5e API client for equipment/metadata enrichment via `PublicDndApiClient`.

## Architecture snapshot
- Entry point: `Program.cs` wires Serilog, controllers, Swagger, SignalR, CORS, health checks, and the global `ExceptionMiddleware`.
- Dependency injection: `Core/ServiceExtensions.cs` registers caching, Mongo context/health check, JWT auth, ownership policy, repositories, domain services, and the D&D API client.
- Services: `Services/*` contains business logic; most inherit `BaseService` for ownership checks, auditing timestamps, and shared CRUD helpers.
- Persistence: `Repositories/*` wraps Mongo collections with caching and soft-delete semantics; `Database/MongoDbContext.cs` configures the client/collections.
- Real-time: `Core/NotificationHub.cs` handles connection grouping by `userId` query string; `Services/SignalR/EntitySyncService.cs` broadcasts fine-grained or batched entity changes.
- Models: `Models/*` and `Authentication/User.cs` hold the domain contracts for characters, monsters, spells, equipment, currency, sessions, campaigns, etc.

## Run it locally
Prereqs: .NET 8 SDK, MongoDB reachable at the configured connection string, and (optional) MongoDB Database Tools if you want backup/restore outside Docker.

1) Copy `.env` (or set environment variables):
```
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://+:5000
AllowedHosts=*
AllowedOrigins=http://localhost:3000

MongoDB__ConnectionString=mongodb://localhost:27017
MongoDB__DatabaseName=dndproject

Jwt__Key=change-me
Jwt__Issuer=dndhelper
Jwt__Audience=dndhelper

DndApi__BaseUrl=https://www.dnd5eapi.co/api/
DndApi__MonstersEndpoint=monsters
DndApi__SpellsEndpoint=spells
DndApi__ClassesEndpoint=classes

# Optional: override if mongodump/mongorestore are not on PATH
MONGODUMP_PATH=/usr/bin/mongodump
MONGORESTORE_PATH=/usr/bin/mongorestore
```
2) Restore & run:
```
dotnet restore
dotnet run --project dndhelper.csproj
```
3) Visit `http://localhost:5000/swagger` (Development only) and `http://localhost:5000/health`.

## Docker
```
docker build -t dndhelper-api .
docker run -p 8080:80 --env-file .env dndhelper-api
```
Mongo Database Tools are baked into the image, so `/api/backup/*` works without extra setup.

## Notable endpoints
- Auth: `POST /api/auth/register`, `POST /api/auth/login`, `GET /api/auth/me`, password change/reset.
- Data CRUD: `/api/characters`, `/api/equipment`, `/api/inventory`, `/api/monsters` (search, paging, ownership helpers), `/api/spells`, `/api/campaign`, `/api/session`, `/api/note`, `/api/currency`.
- Utilities: `/api/dice` (rolls), `/api/cache/info` and `DELETE /api/cache` (admin), `/api/notification` (targeted/all), `/api/backup/{collection}` (export) and `/api/backup/{collection}/restore` (import).
- SignalR: connect to `/hubs/notifications?userId={id}`; listen for `ReceiveNotification`, `EntityChanged`, and `EntityBatchChanged`.

## Logging, errors, and health
- Serilog is configured in `Core/CustomLogger.cs` with structured console/file sinks.
- `ExceptionMiddleware` normalizes errors into JSON with meaningful status codes (400/401/404/409/500).
- `/health` verifies MongoDB via `MongoHealthCheck`.

## Contribution pointers (For myself actually)
- Keep new entities implementing `IEntity` (for auditing) and `IOwnedResource` if they are user-scoped so the ownership policy applies.
- Use the shared `BaseService`/`MongoRepository` for consistency (caching, soft deletes, partial updates).
- Prefer adding new endpoints behind existing services rather than bypassing them to keep logging, auth, and caching intact.
