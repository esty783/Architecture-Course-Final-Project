using Serilog;
using Microsoft.EntityFrameworkCore;
using CatalogService.Data;
using CatalogService.Repository;
using CatalogService.Services;
using CatalogService.Interfaces;
using CatalogService.Middleware;
using SharedModels.Utilities;
using MassTransit;
using StackExchange.Redis;
using CatalogService.Services.Cache;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// ============ SERILOG CONFIGURATION ============
var seqUrl = builder.Configuration["Seq:ServerUrl"] ?? "http://seq:5341";
builder.Host.UseSerilog((context, config) =>
{
    config
        .MinimumLevel.Information()
        .WriteTo.Console()
        .WriteTo.File(
            "logs/catalog-service-.txt",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}"
        )
        .WriteTo.Seq(seqUrl)
        .Enrich.FromLogContext();
});

// ============ DATABASE CONFIGURATION ============
builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.CommandTimeout(30)
    )
);

// ============ MONGODB CONFIGURATION (Polyglot Persistence) ============
// Gifts use MongoDB (document DB) — flexible schema per gift category
// Donors & Categories remain in SQL Server (relational, stable schema)
var mongoConnectionString = builder.Configuration.GetConnectionString("MongoConnection") ?? "mongodb://mongodb:27017";
var mongoClient = new MongoClient(mongoConnectionString);
var mongoDatabase = mongoClient.GetDatabase("Mechira-CatalogService");
builder.Services.AddSingleton<IMongoDatabase>(mongoDatabase);

// ============ DEPENDENCY INJECTION ============
// Repositories
builder.Services.AddScoped<IDonorsRepository, DonorsRepository>();
builder.Services.AddScoped<IGiftsRepository, MongoGiftsRepository>();  // MongoDB
builder.Services.AddScoped<ICategoriesRepository, CategoriesRepository>();

// Services
builder.Services.AddScoped<IDonorsService, DonorsService>();
builder.Services.AddScoped<IGiftsService, GiftsService>();
builder.Services.AddScoped<ICategoriesService, CategoriesService>();

// Utilities
builder.Services.AddSingleton<JwtTokenService>();

// ============ REDIS CACHE CONFIGURATION ============
builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("redis:6379"));
builder.Services.AddScoped<ICacheService, CacheService>();

// ============ MASSTRANSIT CONFIGURATION ============
builder.Services.AddMassTransit(x =>
{
    x.AddConsumers(typeof(Program).Assembly);
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"] ?? "rabbitmq", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"] ?? "guest");
            h.Password(builder.Configuration["RabbitMQ:Password"] ?? "guest");
        });
        cfg.ConfigureEndpoints(context);
    });
});

// ============ CORS CONFIGURATION ============
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });

    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy
            .WithOrigins("http://localhost:4200", "https://localhost:4200")
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// ============ AUTHENTICATION & AUTHORIZATION ============
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"] ?? "your-secret-key-min-32-characters-required-here";

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(jwtSecretKey)
            ),
            ValidateIssuer = true,
            ValidIssuer = "AuthService",
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(5)
        };
    });

builder.Services.AddAuthorization();

// ============ API DOCUMENTATION ============
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "CatalogService API",
        Version = "v1",
        Description = "Product Catalog, Donors, and Categories Management Service"
    });

    // Add JWT Bearer authentication scheme to Swagger
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header using the Bearer scheme"
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });
});

// ============ CONTROLLERS ============
builder.Services.AddControllers();

var app = builder.Build();

// ============ MIDDLEWARE PIPELINE ============
// Correlation ID middleware (must be first)
app.UseMiddleware<CorrelationIdMiddleware>();

// Global exception handling middleware
app.UseMiddleware<GlobalExceptionMiddleware>();

// Logging
app.UseSerilogRequestLogging();

// CORS
app.UseCors("AllowAll");

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// Swagger/OpenAPI
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "CatalogService API v1");
        options.RoutePrefix = "swagger";
    });
}

app.MapControllers();

// Apply database migrations on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        logger.LogInformation("Applying database migrations for CatalogService...");
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations completed for CatalogService");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error applying database migrations for CatalogService");
        throw;
    }
}

app.Run("http://+:5002");
