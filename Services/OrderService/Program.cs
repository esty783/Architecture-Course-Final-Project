using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OrderService.Data;
using OrderService.HttpClients;
using OrderService.Interfaces;
using OrderService.Middleware;
using OrderService.Repository;
using OrderService.Services;
using Serilog;
using SharedModels.Utilities;

var builder = WebApplication.CreateBuilder(args);

// ============ SERILOG CONFIGURATION ============
var seqUrl = builder.Configuration["Seq:ServerUrl"] ?? "http://seq:5341";
builder.Host.UseSerilog((context, config) =>
{
    config
        .MinimumLevel.Information()
        .WriteTo.Console()
        .WriteTo.File(
            "logs/order-service-.txt",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}"
        )
        .WriteTo.Seq(seqUrl)
        .Enrich.FromLogContext();
});

// ============ DATABASE CONFIGURATION ============
builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.CommandTimeout(30)
    )
);

// ============ DEPENDENCY INJECTION ============
// Repositories
builder.Services.AddScoped<IOrdersRepository, OrdersRepository>();

// Services
builder.Services.AddScoped<IOrdersService, OrdersService>();

// Utilities
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddHttpContextAccessor();

// ============ HTTP CLIENT CONFIGURATION WITH POLLY RESILIENCE ============
// AuthService HTTP Client with resilience policies
builder.Services.AddHttpClient<AuthServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:AuthService:Url"] ?? "http://localhost:5001");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(5);
})
    .AddStandardResilienceHandler();

// CatalogService HTTP Client with resilience policies
builder.Services.AddHttpClient<CatalogServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:CatalogService:Url"] ?? "http://localhost:5002");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(5);
})
    .AddStandardResilienceHandler();

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
        cfg.UseConsumeFilter(typeof(CorrelationLogFilter<>), context);
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
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "OrderService API",
        Version = "v1",
        Description = "Order Management Service with inter-service resilience"
    });

    // Add JWT Bearer authentication scheme to Swagger
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header using the Bearer scheme"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
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

// HTTPS redirection
app.UseHttpsRedirection();

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
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "OrderService API v1");
        options.RoutePrefix = "swagger";
    });
}

// Map controllers
app.MapControllers();

// Apply database migrations on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        logger.LogInformation("Applying database migrations for OrderService...");
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations completed for OrderService");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error applying database migrations for OrderService");
        throw;
    }

}

app.Run();

public class CorrelationLogFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", context.CorrelationId))
        {
            await next.Send(context);
        }
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("correlation-log");
}