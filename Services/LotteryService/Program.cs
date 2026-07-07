using Serilog;
using Microsoft.EntityFrameworkCore;
using LotteryService.Data;
using LotteryService.Repository;
using LotteryService.Services;
using LotteryService.Interfaces;
using LotteryService.Middleware;
using LotteryService.Extensions;
using LotteryService.HttpClients;
using SharedModels.Utilities;
using Microsoft.OpenApi.Models;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

// ============ SERILOG CONFIGURATION ============
var seqUrl = builder.Configuration["Seq:ServerUrl"] ?? "http://seq:5341";
builder.Host.UseSerilog((context, config) =>
{
    config
        .MinimumLevel.Information()
        .WriteTo.Console()
        .WriteTo.File(
            "logs/lottery-service-.txt",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}"
        )
        .WriteTo.Seq(seqUrl)
        .Enrich.FromLogContext();
});

// ============ DATABASE CONFIGURATION ============
builder.Services.AddDbContext<LotteryDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.CommandTimeout(30)
    )
);

// ============ DEPENDENCY INJECTION ============
// Repositories
builder.Services.AddScoped<ILotteryRepository, LotteryRepository>();

// Services
builder.Services.AddScoped<ILotteryService, LotteryDrawService>();

// Utilities
builder.Services.AddSingleton<JwtTokenService>();

// ============ HTTP CLIENT CONFIGURATION WITH POLLY RESILIENCE ============
// AuthService HTTP Client with resilience policies
builder.Services.AddHttpClient<AuthServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:AuthService:Url"] ?? "http://localhost:5001");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(5);
})
    .AddResilienceHandler("auth-service-resilience", builder =>
    {
        // Retry policy with exponential backoff
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromSeconds(2),
            ShouldHandle = args => args.Outcome switch
            {
                { Exception: HttpRequestException } => PredicateResult.True(),
                { Result.StatusCode: System.Net.HttpStatusCode.RequestTimeout } => PredicateResult.True(),
                { Result.StatusCode: >= System.Net.HttpStatusCode.InternalServerError } => PredicateResult.True(),
                _ => PredicateResult.False(),
            }
        });
        
        // Circuit breaker policy
        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(30),
            ShouldHandle = args => args.Outcome switch
            {
                { Exception: HttpRequestException } => PredicateResult.True(),
                { Result.StatusCode: >= System.Net.HttpStatusCode.InternalServerError } => PredicateResult.True(),
                _ => PredicateResult.False(),
            }
        });
    });

// CatalogService HTTP Client with resilience policies
builder.Services.AddHttpClient<CatalogServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:CatalogService:Url"] ?? "http://localhost:5002");
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.Timeout = TimeSpan.FromSeconds(5);
})
    .AddResilienceHandler("catalog-service-resilience", builder =>
    {
        // Retry policy with exponential backoff
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromSeconds(2),
            ShouldHandle = args => args.Outcome switch
            {
                { Exception: HttpRequestException } => PredicateResult.True(),
                { Result.StatusCode: System.Net.HttpStatusCode.RequestTimeout } => PredicateResult.True(),
                { Result.StatusCode: >= System.Net.HttpStatusCode.InternalServerError } => PredicateResult.True(),
                _ => PredicateResult.False(),
            }
        });
        
        // Circuit breaker policy
        builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            FailureRatio = 0.5,
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(30),
            ShouldHandle = args => args.Outcome switch
            {
                { Exception: HttpRequestException } => PredicateResult.True(),
                { Result.StatusCode: >= System.Net.HttpStatusCode.InternalServerError } => PredicateResult.True(),
                _ => PredicateResult.False(),
            }
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
        Title = "LotteryService API",
        Version = "v1",
        Description = "Lottery Draw and Ticket Management Service with inter-service resilience"
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

// ============ MESSAGING ============
builder.Services.AddMassTransit(x =>
{
    x.AddConsumers(typeof(Program).Assembly);

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host("rabbitmq-service", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        cfg.ConfigureEndpoints(context);
    });
});

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
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "LotteryService API v1");
        options.RoutePrefix = "swagger";
    });
}
// Map controllers
app.MapControllers();

// Apply database migrations on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<LotteryDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        logger.LogInformation("Applying database migrations for LotteryService...");
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations completed for LotteryService");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error applying database migrations for LotteryService");
        throw;
    }
}

app.Run();
