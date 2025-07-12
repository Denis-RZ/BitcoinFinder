using BitcoinFinderWebServer.Services;
using BitcoinFinderWebServer.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Добавляем CORS для веб-интерфейса
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Регистрируем сервисы
builder.Services.AddSingleton<AgentManager>();
builder.Services.AddSingleton<TaskManager>();
builder.Services.AddSingleton<PoolManager>();
builder.Services.AddSingleton<SeedPhraseFinder>();
builder.Services.AddSingleton<IAgentApiKeyService, AgentApiKeyService>();
builder.Services.AddSingleton<BackgroundSeedTaskManager>();

// Регистрируем сервис базы данных
builder.Services.AddScoped<IDatabaseService, DatabaseService>();

// Регистрируем TCP-совместимый сервис для WinForms-агентов
builder.Services.AddHostedService<TcpCompatibilityService>();

// Добавляем поддержку сессий
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(8);
});

// Регистрируем AuthService
builder.Services.AddSingleton<IAuthService, AuthService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");

// Добавляем поддержку статических файлов
app.UseStaticFiles();

app.UseAuthorization();
app.UseSession();
app.UseMiddleware<BitcoinFinderWebServer.Services.AuthMiddleware>();

// Map controllers
app.MapControllers();

// Добавляем маршрут для веб-интерфейса
app.MapFallbackToFile("database-setup.html");

// Запускаем пул менеджер
var poolManager = app.Services.GetRequiredService<PoolManager>();
_ = Task.Run(() => poolManager.StartAsync());

app.Run();
