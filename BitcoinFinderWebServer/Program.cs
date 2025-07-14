using BitcoinFinderWebServer.Services;
using BitcoinFinderWebServer.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Blazor Server services
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Add authentication and authorization
builder.Services.AddAuthentication("Cookies")
    .AddCookie("Cookies", options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/logout";
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add application services
builder.Services.AddSingleton<AgentManager>();
builder.Services.AddSingleton<TaskManager>();
builder.Services.AddSingleton<PoolManager>();
builder.Services.AddSingleton<SeedPhraseFinder>();
builder.Services.AddSingleton<IAgentApiKeyService, AgentApiKeyService>();
builder.Services.AddSingleton<BackgroundSeedTaskManager>();
builder.Services.AddScoped<IDatabaseService, DatabaseService>();
builder.Services.AddHostedService<TcpCompatibilityService>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(8);
});
builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddHttpClient();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    // app.UseHsts(); // Отключаем HSTS, чтобы не было редиректа на https
}

// app.UseHttpsRedirection(); // Отключаем редирект на https
app.UseCors("AllowAll");
app.UseStaticFiles();
app.UseRouting();

// Add authentication and authorization middleware
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();
app.UseMiddleware<BitcoinFinderWebServer.Services.AuthMiddleware>();

// Map endpoints
app.MapControllers();
app.MapBlazorHub();
app.MapRazorPages();
app.MapFallbackToPage("/_Host");

// Configure URLs
app.Urls.Clear();
app.Urls.Add("http://localhost:5000");

app.Run();
