# Deployment and containerization

Market List ships as one container: Vite compiles the React application, ASP.NET Core serves those static assets and the API, and SQLite stores application data on a persistent volume.

## Build and run with Docker

From the repository root:

```bash
docker build -t market-list:local .
docker run --name market-list \
  --publish 8081:8080 \
  --volume market-list-data:/app/data \
  --restart unless-stopped \
  market-list:local
```

Then open `http://localhost:8081`. The container health check calls `/api/health` inside the container.

The Compose equivalent is:

```bash
docker compose up --build
```

Compose maps host port `8081` to container port `8080` by default. Override only the host side when needed:

```bash
MARKET_LIST_PORT=8090 docker compose up --build
```

Stop it with `docker compose down`. Do not add `--volumes` unless you deliberately want to erase the SQLite database.

## Data and backups

The database lives at `/app/data/groceries.db`. That directory must be mounted to a durable volume. The application creates the schema on first start. Existing prototype databases are upgraded at startup with the household-category table and default categories; back up the volume before deploying a version that changes the schema.

Create a consistent backup by stopping writes first:

```bash
docker compose stop groceries
docker run --rm \
  --volume groceries_grocery-data:/source:ro \
  --volume "$PWD/backups:/backup" \
  alpine cp /source/groceries.db /backup/groceries-$(date +%F).db
docker compose start groceries
```

The exact Compose volume prefix is normally the repository directory name. Confirm it with `docker volume ls` before running a backup command. Store backups somewhere other than the application host and test restoring them periodically.

## Deploying to a container host

This image works on hosts that provide:

- HTTPS termination and forwarding to port `8080`
- A persistent disk mounted at `/app/data`
- Health checks against `GET /api/health`
- A single running application replica

SQLite is intentionally a single-node database. Do not run multiple replicas against the same file over a network filesystem. For horizontal scaling, move the EF Core provider to PostgreSQL or SQL Server first.

A generic registry workflow is:

```bash
docker build --platform linux/amd64 -t registry.example.com/market-list:1.0.0 .
docker push registry.example.com/market-list:1.0.0
```

On the host, configure:

| Setting | Value |
| --- | --- |
| Container port | `8080` |
| Persistent mount | `/app/data` |
| Health path | `/api/health` |
| Environment | `ASPNETCORE_ENVIRONMENT=Production` |
| Database | `ConnectionStrings__Default=Data Source=/app/data/groceries.db` |
| Session keys | `DataProtection__Path=/app/data/keys` |

Terminate TLS at the platform load balancer or reverse proxy. Keep the application and API on the same public origin so the HTTP-only authentication cookie works without CORS configuration. Data-protection keys share the persistent data volume so sessions survive container restarts; protect that volume as credential material.

## Reverse proxy notes

If a proxy terminates HTTPS, configure it to pass `X-Forwarded-For` and `X-Forwarded-Proto`. Configure ASP.NET Core to trust only the proxy's network before enabling forwarded headers. Avoid trusting forwarded headers from arbitrary clients.

Cookie sessions are HTTP-only and `SameSite=Lax`. Before an internet-facing launch, change the production cookie policy to `Secure=Always` after forwarded HTTPS is configured and add antiforgery protection to all state-changing endpoints.

## Database evolution

The current prototype uses `Database.EnsureCreatedAsync()` for a zero-command first launch. Before the first production deployment:

1. Replace `EnsureCreatedAsync()` with `MigrateAsync()`.
2. Add `Microsoft.EntityFrameworkCore.Design` and the matching `dotnet-ef` tool.
3. Create and commit an initial migration.
4. Back up the database before every schema migration.
5. Run migrations as a one-off release job before starting the new application version.

Do not mix `EnsureCreated` and EF migrations against the same production database.

## Production checklist

- Upgrade the project and base images to .NET 10 LTS before a long-lived deployment.
- Pin Node and .NET image digests in the deployment pipeline.
- Put the service behind HTTPS and enable secure cookies.
- Add antiforgery tokens for cookie-authenticated write requests.
- Add email confirmation and a password-reset flow.
- Add login throttling or account lockout.
- Mount and monitor a persistent disk with automated off-host backups.
- Centralize logs and alert on failed health checks.
- Run the container as its included non-root user.
- Scan the image and dependencies in CI.

## CI outline

A future pipeline should run these gates in order:

```bash
npm ci --prefix client
npm run build --prefix client
dotnet restore Groceries.sln
dotnet build Groceries.sln --configuration Release --no-restore
docker build -t market-list:${GIT_SHA} .
```

After tests are added, place `dotnet test` and the client test command before the image build. Push immutable version tags, deploy one replica, run a health check, and retain the previous image for rollback.
