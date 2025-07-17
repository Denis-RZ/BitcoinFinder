using BitcoinFinderWebServer.Services;
using BitcoinFinderWebServer.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Add session support
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Регистрируем сервисы
builder.Services.AddSingleton<SeedPhraseFinder>();
builder.Services.AddSingleton<TaskStorageService>();
builder.Services.AddSingleton<TaskManager>();
builder.Services.AddSingleton<AgentManager>();
builder.Services.AddSingleton<PoolManager>();
builder.Services.AddSingleton<AgentApiKeyService>();
builder.Services.AddSingleton<TcpCompatibilityService>();
// Регистрируем AuthService для IAuthService
builder.Services.AddSingleton<IAuthService, AuthService>();
// Регистрируем BackgroundTaskService
builder.Services.AddSingleton<IBackgroundTaskService, BackgroundTaskService>();
builder.Services.AddHostedService<BackgroundTaskService>();

// Настройка логирования
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Logging.SetMinimumLevel(LogLevel.Information);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Add session middleware
app.UseSession();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
