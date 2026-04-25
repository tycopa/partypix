using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data;
using PartyPix.Web.Data.Entities;
using PartyPix.Web.Hubs;
using PartyPix.Web.Middleware;
using PartyPix.Web.Services;
using Serilog;
using tusdotnet;
using tusdotnet.Models;
using tusdotnet.Models.Configuration;
using tusdotnet.Stores;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("App_Data/logs/partypix-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();
builder.Host.UseSerilog();

// Hosted in-process by IIS via the ASP.NET Core Module (see web.config).
// IIS terminates HTTP and forwards into the app; Kestrel is not used.

// -- Database --------------------------------------------------------------
var connStr = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default not configured");

builder.Services.AddDbContext<AppDbContext>(opts => opts.UseSqlServer(connStr));

// -- Identity (hosts only) ------------------------------------------------
builder.Services.AddDefaultIdentity<AppUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// DELETE from the admin lightbox carries the antiforgery token in this
// header instead of a form field. AddAntiforgery is called implicitly by
// AddRazorPages; we just rename the header so our fetch() call is explicit.
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");

// -- App services ---------------------------------------------------------
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.AddSingleton<IStorageService, LocalDiskStorage>();
builder.Services.AddScoped<ImageProcessor>();
builder.Services.AddScoped<VideoProcessor>();
builder.Services.AddSingleton<QrService>();
builder.Services.AddScoped<GuestSessionAccessor>();
builder.Services.AddScoped<MediaNotifier>();
builder.Services.AddScoped<TusUploadHandler>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/Admin");
});
builder.Services.AddSignalR();
builder.Services.AddControllers();

// Trust proxy headers. Cloudflare Tunnel is effectively the only proxy in
// front, and it connects to us on localhost. Accept X-Forwarded-For only from
// loopback (127.0.0.0/8 and ::1) so direct/untrusted requests cannot spoof
// client IP or protocol. CF-Connecting-IP is also promoted by the middleware
// below, which gates promotion on RemoteIpAddress being loopback.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    o.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Loopback, 8));       // 127.0.0.0/8
    o.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.IPv6Loopback, 128)); // ::1/128
    o.ForwardLimit = 1;
});

// Allow large uploads. With in-process IIS hosting this IISServerOptions
// value governs body size; the matching web.config value is
// system.webServer/security/requestFiltering/requestLimits/@maxAllowedContentLength.
// tus chunks stay under Cloudflare's 100MB body limit regardless.
// Read the limit once so IIS and tus always use the same effective value.
const long DefaultMaxUploadBytes = 524288000; // 500 MB
var maxUploadBytes = builder.Configuration.GetValue<long?>("Tus:MaxUploadBytes") ?? DefaultMaxUploadBytes;
// tusdotnet's MaxAllowedUploadSizeInBytes is int?, so clamp to int.MaxValue.
var maxUploadBytesForTus = (int)Math.Min(maxUploadBytes, int.MaxValue);

builder.Services.Configure<IISServerOptions>(o =>
{
    o.MaxRequestBodySize = maxUploadBytes;
});

var app = builder.Build();

// -- Middleware pipeline --------------------------------------------------
app.UseForwardedHeaders();
app.UseMiddleware<CloudflareClientIpMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Error/{0}");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// -- Tus upload endpoint --------------------------------------------------
var tusTempPath = Path.Combine(builder.Environment.ContentRootPath,
    builder.Configuration["Tus:TempPath"] ?? "App_Data/tus");
Directory.CreateDirectory(tusTempPath);

app.MapTus("/api/uploads", async httpContext => new DefaultTusConfiguration
{
    Store = new TusDiskStore(tusTempPath),
    MaxAllowedUploadSizeInBytes = maxUploadBytesForTus,
    Events = new Events
    {
        OnFileCompleteAsync = async ctx =>
        {
            var handler = httpContext.RequestServices.GetRequiredService<TusUploadHandler>();
            await handler.OnFileCompletedAsync(ctx);
        },
    },
});

app.MapRazorPages();
app.MapControllers();
app.MapHub<SlideshowHub>("/hubs/slideshow");

// Apply any pending EF Core migrations on every startup.
// Migrate() is idempotent: it creates the database if it does not exist and
// then applies every migration that has not yet been applied, so it is safe
// in both development and production. This runs on every deploy/publish,
// ensuring the schema is always up-to-date without any manual steps.
// Note: SQL Server serialises concurrent migration attempts with an application
// lock, so simultaneous restarts of multiple instances will not corrupt the
// schema. Startup time is negligible when there are no pending migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Ensure the Admin role exists so the first-registered user can be
    // placed in it as the bootstrap admin. Registration is otherwise
    // locked to signed-in admins (see Areas/Identity/Pages/Account/Register).
    var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    if (!roleMgr.RoleExistsAsync("Admin").GetAwaiter().GetResult())
    {
        roleMgr.CreateAsync(new IdentityRole("Admin")).GetAwaiter().GetResult();
    }

    // One-shot rescue for accounts that registered before the role gate
    // shipped: if no user is currently in the Admin role and exactly one
    // user account exists, promote that lone account so the deployer can
    // actually invite others. Multi-user installs are left alone — picking
    // the admin in that case is a deliberate decision, not something to
    // auto-resolve.
    var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var existingAdmins = userMgr.GetUsersInRoleAsync("Admin").GetAwaiter().GetResult();
    if (existingAdmins.Count == 0)
    {
        var allUsers = userMgr.Users.ToList();
        if (allUsers.Count == 1)
        {
            userMgr.AddToRoleAsync(allUsers[0], "Admin").GetAwaiter().GetResult();
        }
    }
}

app.Run();
