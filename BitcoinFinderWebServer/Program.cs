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

// Регистрируем TCP-совместимый сервис для WinForms-агентов
builder.Services.AddHostedService<TcpCompatibilityService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();

// Map controllers
app.MapControllers();

// Запускаем пул менеджер
var poolManager = app.Services.GetRequiredService<PoolManager>();
_ = Task.Run(() => poolManager.StartAsync());

app.Run();
