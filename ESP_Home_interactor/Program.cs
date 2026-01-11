using ESP_Home_Interactor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services for Blazor Server
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Add ESP Device Service
builder.Services.AddHostedService<EspDeviceService>();
builder.Services.AddSingleton<EspDeviceService>(sp =>
    sp.GetServices<IHostedService>().OfType<EspDeviceService>().First());

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
