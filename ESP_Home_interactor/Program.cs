using ESP_Home_Interactor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services for Blazor Server
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Add ESP Device Service
builder.Services.AddSingleton<EspDeviceService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EspDeviceService>());

// Add Cycle Scheduler
builder.Services.AddSingleton<CycleSchedulerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CycleSchedulerService>());

// Add AC Infinity Service
builder.Services.AddSingleton<AcInfinityService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AcInfinityService>());

// Add Sensor History
builder.Services.AddSingleton<SensorHistoryService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SensorHistoryService>());

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
