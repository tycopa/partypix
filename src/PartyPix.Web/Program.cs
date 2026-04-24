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
    .AddEntityFrameworkStores<AppDbContext>();

// -- App services ---------------------------------------------------------
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.AddSingleton<IStorageService, LocalDiskStorage>();
builder.Services.AddScoped<ImageProcessor>();
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
// front, and it connects to us on localhost. Accept X-Forwarded-For from it
// and also promote CF-Connecting-IP via middleware below.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// Allow large uploads. With in-process IIS hosting this IISServerOptions
// value governs body size; the matching web.config value is
// system.webServer/security/requestFiltering/requestLimits/@maxAllowedContentLength.
// tus chunks stay under Cloudflare's 100MB body limit regardless.
builder.Services.Configure<IISServerOptions>(o =>
{
    o.MaxRequestBodySize = builder.Configuration.GetValue<long?>("Tus:MaxUploadBytes") ?? 524288000;
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
    MaxAllowedUploadSizeInBytes = app.Configuration.GetValue<long?>("Tus:MaxUploadBytes"),
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

// Ensure DB exists on startup (dev-friendly; use migrations in prod)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

app.Run();
