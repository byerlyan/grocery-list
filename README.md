# Grocery List

A shared grocery list built with React, TypeScript, ASP.NET Core, Identity, Entity Framework Core, and SQLite.

## Demo images
- Located under docs/images
## Features

- Create an account and a private household
- Share a household using a rotatable invite code
- Create, rename, and remove shared household categories
- Add, edit, categorize, check off, and remove groceries together
- Filter active and completed items
- Persist accounts and lists in SQLite
- Serve the React application and API from one ASP.NET Core process
- Package the whole application as a non-root container

## Run locally

Install and build the React client:

```bash
cd client
npm install
npm run build
cd ..
```

Start the application:

```bash
dotnet run --project server
```

Open `http://localhost:5098`. The SQLite database is created at `server/data/groceries.db` and is ignored by Git.

For frontend development with hot reload, leave the API running and start Vite in another terminal:

```bash
cd client
npm run dev
```

Open `http://localhost:5173`; Vite proxies `/api` to ASP.NET Core.

## Try household sharing

1. Register the first account and copy its household invite code.
2. Sign out and register a second account with another email address.
3. Enter the first household's invite code in the shared-household card.
4. Both accounts now read and update the same grocery list.

Joining another household removes an account's original empty household. If that original household contains other members, it remains available to them. A sole member must clear their existing list before moving, which prevents groceries from being discarded accidentally.

## Container

```bash
docker compose up --build
```

Open `http://localhost:8081`. SQLite is stored in the named `grocery-data` volume. To use another host port, set it when starting Compose—for example, `MARKET_LIST_PORT=8090 docker compose up --build`.

See [docs/deployment.md](docs/deployment.md) for image builds, persistent storage, backups, reverse-proxy guidance, production hardening, and a CI outline.

## Project structure

```text
client/              React + TypeScript UI
server/              ASP.NET Core API, Identity, and EF Core model
server/data/         Local SQLite storage (ignored)
docs/deployment.md   Container and deployment runbook
Dockerfile           Multi-stage production image
compose.yaml         Local container stack with persistent storage
```

## API overview

| Area | Endpoints |
| --- | --- |
| Accounts | `POST /api/auth/register`, `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me` |
| Household | `POST /api/household/join`, `POST /api/household/invite-code` |
| Categories | `GET/POST /api/categories`, `PUT/DELETE /api/categories/{id}` |
| Groceries | `GET/POST /api/groceries`, `PUT/DELETE /api/groceries/{id}`, `DELETE /api/groceries/completed` |
| Operations | `GET /api/health` |

All household and grocery endpoints require the HTTP-only session cookie.

## Current deployment boundary

This version is ready for local use and a private single-instance deployment. Before exposing it publicly, follow the hardening checklist in the deployment guide—especially HTTPS, secure cookies, antiforgery protection, password reset, and database migrations.
