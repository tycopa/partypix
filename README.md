# PartyPix

QR-code event photo sharing. Guests scan a code, add their name, and drop
photos and videos into a shared gallery. Hosts get a live slideshow for the
big screen and a full-resolution ZIP of everything after the event.

Built to run on a Windows VM behind a Cloudflare Tunnel.

## Stack

- **ASP.NET Core 10** (Razor Pages) — hosted **in-process by IIS** via the
  ASP.NET Core Module V2
- **SQL Server** via EF Core (LocalDB in dev, SQL Server / SQL Express in prod)
- **SignalR** for the live slideshow and gallery updates
- **tusdotnet** for resumable chunked uploads (keeps chunks under Cloudflare's
  100 MB body limit)
- **SixLabors.ImageSharp** for thumbnails, display variants, and EXIF GPS
  scrubbing
- **QRCoder** for QR generation
- **Tailwind CSS**, HTMX, Alpine.js, tus-js-client on the front end
- **Cloudflare Tunnel** as the public edge (TLS, DDoS, caching)

## Layout

```
src/PartyPix.Web/
  Program.cs                  Wires DI, middleware, tus, SignalR
  Data/                       EF Core DbContext + entities
  Services/                   Storage, image pipeline, QR, guest sessions
  Controllers/                /media, /qr, /download (ZIP)
  Hubs/SlideshowHub.cs        SignalR hub (per-event groups)
  Middleware/                 CF-Connecting-IP promotion
  Pages/
    Index.cshtml              Landing
    Admin/                    Host dashboard, create event, event detail
    E/                        Guest: welcome → gallery → upload → slideshow
  Styles/site.css             Tailwind entry
  wwwroot/css/site.css        Tailwind output (gitignored)
  web.config                  IIS hosting config (AspNetCoreModuleV2)
scripts/
  install-iis.ps1             Create IIS app pool + site + folder ACLs
  cloudflared-config.example.yml
```

## Local development

Prereqs: .NET 10 SDK, Node 20+, SQL Server LocalDB (installed with Visual
Studio or via the standalone LocalDB installer).

```powershell
cd src\PartyPix.Web
npm install
npm run build              # compiles Tailwind → wwwroot/css/site.css
dotnet run
```

App listens on `http://localhost:5000`. The dev connection string targets
`(localdb)\MSSQLLocalDB`; the database is created on first run. Uploads
and logs live under `src/PartyPix.Web/App_Data/`. Register a host account,
create an event, scan the QR code on your phone, and test the full flow
end-to-end.

Tailwind watcher (in a second terminal):
```powershell
npm run dev
```

## Automated deployment with GitHub Actions

A workflow at `.github/workflows/deploy-to-iis.yml` deploys the application to
an IIS server automatically on every push to `main` (or manually via
**Actions → Deploy to IIS → Run workflow**).

### How it works

1. The workflow runs on a **Windows self-hosted runner** registered on the IIS
   host.
2. It builds the Tailwind CSS bundle with Node.js 20 and publishes the ASP.NET
   Core app with `dotnet publish -c Release`.
3. It stops the IIS Application Pool, mirrors the published output to the
   configured deploy path with `robocopy`, then restarts the pool — causing
   brief downtime during the file sync and application restart.

### Runner setup

1. On the IIS host, follow the GitHub docs to
   [add a self-hosted runner](https://docs.github.com/en/actions/hosting-your-own-runners/adding-self-hosted-runners).
2. Ensure the runner service account has:
   - Write access to the IIS deploy directory.
   - Permission to stop/start the IIS Application Pool.
     (`IIS AppPool\<pool-name>` or an administrator account).
3. Install prerequisites on the host:
   - **.NET 9 Hosting Bundle** (includes the ASP.NET Core Module for IIS)
   - **Node.js 20+**

### Required secrets

Add these in **Settings → Secrets and variables → Actions**:

| Secret | Example value | Description |
|---|---|---|
| `IIS_DEPLOY_PATH` | `C:\daubery\partypix` | Absolute path IIS serves the app from |
| `IIS_APP_POOL_NAME` | `partypix` | Name of the IIS Application Pool |

### IIS site assumptions

- An IIS **Web Site** and **Application Pool** already exist and point at
  `IIS_DEPLOY_PATH`.
- The Application Pool's **.NET CLR version** is set to **"No Managed Code"**
  (ASP.NET Core runs out-of-process via the ASP.NET Core Module).
- The pool identity has write access to `App_Data\` inside the deploy path
  (for the SQLite database and uploads).
- `web.config` is generated automatically by `dotnet publish` — no manual IIS
  handler mapping is needed.

---

## Production deployment on a Windows VM

### 1. Prepare the VM

Enable IIS with the WebSocket feature (PowerShell, elevated):

```powershell
Install-WindowsFeature `
    Web-Server, Web-WebSockets, Web-Http-Logging, Web-Stat-Compression, `
    Web-Static-Content, Web-Default-Doc, Web-Http-Errors, `
    Web-Request-Monitor, Web-Filtering
```

Then install:

- **.NET 10 Hosting Bundle** — provides the ASP.NET Core Module V2 that IIS
  uses to run the app in-process. Install *after* IIS so the module
  registers. If you install it first, run
  `dotnet-hosting-10.0.0-win.exe OPT_NO_SHARED_CONFIG_CHECK=1` or repair it
  after IIS is on.
- **SQL Server Express** (or a full SQL Server instance) — required.
  Create an empty database named `PartyPix` (or let the app create it via
  `EnsureCreated` on first run).
- **ffmpeg** on PATH (for the eventual video pipeline — not required for v0.1)
- **cloudflared** for Windows

### 2. Publish

From your dev box:
```powershell
dotnet publish src\PartyPix.Web\PartyPix.Web.csproj -c Release -o C:\apps\partypix
```

Copy the output to the VM (or build on the VM). The publish output includes
the shipped `web.config` — `<IsTransformWebConfigDisabled>true</IsTransformWebConfigDisabled>`
in the csproj prevents publish from overwriting it.

### 3. Configure

Edit `C:\apps\partypix\appsettings.Production.json`:

```json
{
  "ConnectionStrings": {
    "Default": "Server=localhost\\SQLEXPRESS;Database=PartyPix;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "Storage": { "RootPath": "D:\\partypix-media" },
  "Tus":     { "TempPath": "D:\\partypix-tus", "MaxUploadBytes": 524288000 },
  "PublicBaseUrl": "https://partypix.example.com"
}
```

If SQL Server is running under a different identity than the IIS app
pool (the default), grant `IIS AppPool\PartyPix` a SQL login and
`db_owner` on the `PartyPix` database, or use a SQL auth connection
string with `User Id=... ;Password=...` instead of `Trusted_Connection`.

IIS handles port binding; don't set a Kestrel endpoint here.

### 4. Create the IIS site

```powershell
.\scripts\install-iis.ps1 -PublishPath C:\apps\partypix -Port 5000
```

The script:
- Creates an app pool **PartyPix** with `managedRuntimeVersion=""` (No Managed
  Code — required for ASP.NET Core via AspNetCoreModuleV2) and
  `startMode=AlwaysRunning` so the app doesn't cold-start after idle.
- Creates a site **PartyPix** bound to `127.0.0.1:5000`, so the only way to
  reach it from outside the VM is cloudflared.
- Grants `IIS AppPool\PartyPix` Modify on the publish folder so
  App_Data logs and any in-folder storage/tus paths work.

If `Storage:RootPath` or `Tus:TempPath` point outside the publish folder
(e.g. `D:\partypix-media`), grant `IIS AppPool\PartyPix` Modify on those
folders too.

Verify it's alive:
```powershell
curl http://127.0.0.1:5000/
```

### 5. Configure the Cloudflare Tunnel

From the Cloudflare Zero Trust dashboard:

1. **Networks → Tunnels → Create a tunnel** (Cloudflared)
2. Copy the install command and run it on the VM; it registers
   `cloudflared` as a Windows Service.
3. **Public Hostname**: `partypix.example.com` → `http://localhost:5000`
4. Under **Additional application settings → TLS**, leave "No TLS Verify"
   enabled (origin is HTTP on localhost).
5. Under **Additional application settings → HTTP Settings**, confirm
   `HTTP2 connection to origin` is on (SignalR WebSockets work with HTTP/1.1
   Upgrade as well).

Or use the config file at `scripts/cloudflared-config.example.yml`.

### 6. (Optional) Lock down /Admin with Cloudflare Access

In Zero Trust → Access → Applications, add a self-hosted application for
`partypix.example.com/Admin*` and require one-time PIN / Google SSO. Leave
`/e/*`, `/media/*`, `/qr/*`, `/api/uploads*`, `/hubs/*` public.

## The Cloudflare 100 MB limit

Cloudflare caps request bodies at **100 MB on Free/Pro, 200 MB on Business,
500 MB on Enterprise**. A 4K iPhone video easily exceeds 100 MB. Uploads
therefore go through **tus** with 5 MB chunks — each chunk request stays well
under the limit, and uploads resume automatically on flaky cellular.

No other config is needed on the Cloudflare side; tus is just HTTP `PATCH`
requests.

## Upload size limits on IIS

Three knobs have to agree or uploads will 413:

| Layer | Setting | Value |
|---|---|---|
| IIS request filtering | `web.config` → `requestLimits/@maxAllowedContentLength` | 524288000 |
| ASP.NET Core | `IISServerOptions.MaxRequestBodySize` (`Program.cs`) | 524288000 |
| Cloudflare (edge) | plan-dependent | 100 MB (Free/Pro) |

`Tus:MaxUploadBytes` in `appsettings.json` drives the ASP.NET Core limit and
the tus ceiling; it must be ≤ the `web.config` value. The web.config value
is the hard IIS ceiling and needs to be set manually.

## Media & storage layout

```
<Storage:RootPath>/
  events/
    <slug>/
      orig/<mediaId>.<ext>     # source file
      display/<mediaId>.jpg    # max 2000 px
      thumb/<mediaId>.jpg      # max 512 px
```

`GET /media/<id>/thumb|display|original` is gated by guest session or host
ownership. Thumbs/displays are served with a 1-year `Cache-Control`, so
Cloudflare caches them at the edge after the first request.

Originals are downloaded as a streamed ZIP via `GET /download/<slug>.zip`
(host only).

## What's implemented in v0.1

- [x] Host signup + login (ASP.NET Identity)
- [x] Create event (title, date, welcome message, optional access code)
- [x] Unique slug generation, share link, QR (PNG + SVG)
- [x] Guest welcome page → name capture → cookie session
- [x] Guest upload via tus (chunked, resumable)
- [x] Image processing: display + thumbnail + EXIF GPS scrub
- [x] Live gallery (SignalR)
- [x] Live slideshow (SignalR, auto-advances every 5 s)
- [x] Host event dashboard with recent uploads, settings, QR
- [x] Gated `/media/*` delivery with CDN-friendly caching
- [x] Streamed ZIP download for host

## Deliberately not in v0.1

- Video transcoding + poster frames (videos upload and display as originals;
  add a background worker with ffmpeg to generate posters)
- Video guestbook
- Likes / comments (schema is in place via `Reaction`)
- Themes / custom branding (single default theme for now)
- Stripe / pricing tiers
- Albums UI (schema exists)
- Co-hosts
- Moderation tools beyond "hide"

Each of these is isolated from the v0.1 code paths and can be added without
reshaping the core.
