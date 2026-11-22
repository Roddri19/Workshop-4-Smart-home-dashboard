using System.Net.Security;
using HomeManagement.Api.Dashboard;
using HomeManagement.Api.Data;
using HomeManagement.Api.Devices;
using HomeManagement.Api.Monitoring;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Database: a single SQLite file, nothing to install.
builder.Services.AddDbContext<HomeDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Home") ?? "Data Source=smarthome.db"));

// HttpClient used to talk to the devices.
var deviceHttp = builder.Services.AddHttpClient<DeviceClient>(client => client.Timeout = TimeSpan.FromSeconds(5));

// The emulators use the local ASP.NET development certificate. If it is not trusted on this machine
// (dotnet dev-certs https --trust), accept it anyway, but only for localhost and only in Development.
if (builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue("Devices:AcceptLocalhostDevCertificate", false))
{
    deviceHttp.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
            errors == SslPolicyErrors.None || request.RequestUri?.IsLoopback == true
    });
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<DeviceStatusService>();
builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection("Monitoring"));
builder.Services.AddHostedService<DeviceMonitor>();
builder.Services.AddProblemDetails();

var app = builder.Build();

await app.InitializeDatabaseAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();

// The dashboard: wwwroot/index.html is served at "/".
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapDeviceEndpoints();
app.MapDashboardEndpoints();

app.Run();
