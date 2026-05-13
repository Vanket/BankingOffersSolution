using BankingOffers.API.Configuration;
using BankingOffers.API.Data;
using BankingOffers.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// --- 1. НАСТРОЙКА БАЗЫ ДАННЫХ (SQLite) ---
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=banking.db"));

// --- 2. НАСТРОЙКА КОНФИГУРАЦИИ ПАРСИНГА ---
builder.Services.Configure<List<ScrapingTarget>>(builder.Configuration.GetSection("ScrapingTargets"));

// --- 3. РЕГИСТРАЦИЯ КОНТРОЛЛЕРОВ И SWAGGER ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// --- 4. РЕГИСТРАЦИЯ ФОНОВОГО СЕРВИСА (ПАРСЕР) ---
builder.Services.AddHostedService<BankScraperService>();

// --- 5. НАСТРОЙКА АВТОРИЗАЦИИ (JWT) ---
// Этот блок учит сервер понимать и проверять токены пользователей
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8
                .GetBytes(builder.Configuration.GetSection("AppSettings:Token").Value!)),
            ValidateIssuer = false, 
            ValidateAudience = false 
        };
    });

// --- 6. НАСТРОЙКА CORS ---
// Разрешаем Frontend-приложению делать запросы к нашему API
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorApp", policy =>
    {
        policy.AllowAnyOrigin() // Разрешаем всем (для простоты)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// --- 7. АВТОМАТИЧЕСКОЕ ПРИМЕНЕНИЕ МИГРАЦИЙ ---
// При запуске сервер сам обновит базу данных, если она устарела
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

// --- 8. НАСТРОЙКА ПАЙПЛАЙНА ЗАПРОСОВ ---
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Важен порядок: Сначала CORS, потом Аутентификация, потом Авторизация
app.UseCors("AllowBlazorApp");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();