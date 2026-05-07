# Local development environment

End-to-end setup for running BookTracker on a fresh machine. Windows is the supported dev OS (PowerShell 7+); the steps below assume that. Linux/macOS would work with the same tools but the commands haven't been validated.

## Prerequisites

| Tool | Why | Install |
|---|---|---|
| .NET 10 SDK | Build + run the app and EF migrations | `winget install Microsoft.DotNet.SDK.10` |
| Docker Desktop | Hosts the SQL Server + Azurite containers | https://www.docker.com/products/docker-desktop |
| PowerShell 7+ | Default shell for repo commands | `winget install Microsoft.PowerShell` |
| `mkcert` | Generates a locally-trusted TLS cert for Azurite so dev parity matches prod (HTTPS both ends) | `winget install FiloSottile.mkcert` |
| `git` (with line-ending defaults) | Source control | https://git-scm.com/download/win |

The repo path contains a space (`...\code\The Library\`) — every command quotes it.

## One-time setup

Run these once after cloning. Subsequent dev sessions skip to **Daily workflow** below.

### 1. Install the local TLS root cert

mkcert manages a local certificate authority that your OS trusts. The Azurite container will serve TLS using a cert signed by this CA, and the .NET app's `BlobServiceClient` (which uses the Windows system trust store) will trust it without any code-side `ServerCertificateValidationCallback` workaround.

```powershell
mkcert -install
```

You'll see a confirmation that the local CA was added to the Windows trust store. This is per-machine — anyone else cloning the repo runs this same command.

### 2. Generate the Azurite cert

From the repo root:

```powershell
New-Item -ItemType Directory -Path .\azurite-certs -Force | Out-Null
Set-Location .\azurite-certs
mkcert -cert-file localhost.pem -key-file localhost-key.pem localhost 127.0.0.1 ::1
Set-Location ..
```

Two files land in `azurite-certs/` (gitignored). Docker Compose mounts this directory read-only into the Azurite container.

### 3. Copy the appsettings templates

`appsettings.json` and `appsettings.Development.json` are gitignored; their committed templates have the `.Example` suffix. Copy and edit:

```powershell
Copy-Item BookTracker.Web\appsettings.Example.json BookTracker.Web\appsettings.json
Copy-Item BookTracker.Web\appsettings.Development.Example.json BookTracker.Web\appsettings.Development.json
```

The Development template already points at the local SQL container and the HTTPS Azurite endpoint with the well-known dev account key — no edits needed for the cover-storage path. Optional secrets (Anthropic / Trove API keys) can be filled in if you want those features locally.

### 4. Start the containers

```powershell
docker compose up -d
```

This brings up two services:

- **`booktracker-db`** — SQL Server 2022 Developer on `localhost:1433`. SA password defaults to `BookTracker!Dev1` (override via `$env:MSSQL_SA_PASSWORD = "..."` before `docker compose up` if you want a different one). Persists in the `booktracker-db-data` named Docker volume.
- **`booktracker-azurite`** — Azure Storage emulator on `localhost:10000`, serving HTTPS using the cert you generated in step 2. Persists blob data to `./azurite-data/` on the host (gitignored, bind-mounted so files are inspectable in the file explorer).

### 5. Initialise the database

EF Core migrations are run from `BookTracker.Data` with `BookTracker.Web` as the startup project so configuration + connection string resolve:

```powershell
dotnet ef database update --project .\BookTracker.Data --startup-project .\BookTracker.Web
```

## Daily workflow

```powershell
docker compose up -d                                  # start containers (idempotent)
dotnet watch --project .\BookTracker.Web              # hot-reload dev server
```

The app comes up at `https://localhost:7XXX` (the port shows in the console). Inside the running app:

- Cover images mirror to Azurite as you add books — within ~30s of save you should see `Mirrored cover for Edition {N}` log lines and the image URLs in the DB swap to `https://localhost:10000/devstoreaccount1/book-covers/...`.
- All AI providers and the Trove ISBN lookup require API keys in `appsettings.Development.json` to actually run; without keys, the providers cleanly drop out of the picker.

## Stopping

```powershell
docker compose down            # stop containers, keep data
docker compose down -v         # stop AND wipe the SQL volume (Azurite data persists in azurite-data/ either way)
```

To wipe Azurite data too:

```powershell
docker compose down
Remove-Item -Recurse -Force .\azurite-data
```

## EF migrations

Adding or rolling back a migration always needs both `--project` (Data) and `--startup-project` (Web):

```powershell
dotnet ef migrations add <Name>   --project .\BookTracker.Data --startup-project .\BookTracker.Web
dotnet ef database update         --project .\BookTracker.Data --startup-project .\BookTracker.Web
dotnet ef migrations remove       --project .\BookTracker.Data --startup-project .\BookTracker.Web
```

## Common gotchas

- **`mkcert -install` skipped.** Symptom: `BlobServiceClient` throws TLS validation errors when the background service tries to upload. Run `mkcert -install` once per machine and restart the app.
- **Azurite cert files missing.** Symptom: container fails to start or exits immediately. Check `docker logs booktracker-azurite` — should reference `/certs/localhost.pem`. Re-run step 2 if the files aren't there.
- **`appsettings.Development.json` not loading.** Symptom: `[CoverStorage] ConnectionString length: -1` style logs, or services log "disabled". Check that `$env:ASPNETCORE_ENVIRONMENT` is `Development` (Rider/VS default) and that the `CoverStorage` block is at the top level, not nested inside another section.
- **Repo path with space.** Always quote `"path\to\The Library"` in shell commands. PowerShell tab-completion handles it; manual paths break without quotes.

## Production deployment

Local dev is the focus of this doc. Production deployment via `infra/` (Bicep) and `.github/workflows/deploy.yml` is documented separately in [`infra/README.md`](../infra/README.md).
