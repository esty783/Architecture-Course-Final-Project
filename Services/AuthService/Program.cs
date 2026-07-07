using Serilog;
using Microsoft.EntityFrameworkCore;
using AuthService.Data;
using AuthService.Repository;
using AuthService.Services;
using AuthService.Interfaces;
using SharedModels.Interfaces;
using AuthService.Middleware;
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
            "logs/auth-service-.txt",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}"
        )
        .WriteTo.Seq(seqUrl)
        .Enrich.FromLogContext();
});

// ============ DATABASE CONFIGURATION ============
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.CommandTimeout(30)
    )
);

// ============ DEPENDENCY INJECTION ============
// Repositories
builder.Services.AddScoped<IUsersRepository, UsersRepository>();

// Services
builder.Services.AddScoped<IAuthService, UsersService>();
builder.Services.AddScoped<IUsersService, UsersService>();

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
var jwtSecretKey = builder.Configuration["Jwt:SecretKey"] ?? "your-super-secret-key-that-must-be-at-least-32-characters-long-for-hmac256";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "AuthService";
var jwtExpiryMinutes = int.Parse(builder.Configuration["Jwt:ExpiryMinutes"] ?? "60");

// Utilities - Register after getting config values
builder.Services.AddSingleton(new JwtTokenService(jwtSecretKey, jwtIssuer, jwtExpiryMinutes));

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
        Title = "AuthService API",
        Version = "v1",
        Description = "Authentication and User Management Service"
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
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "AuthService API v1");
        options.RoutePrefix = "swagger";
    });
}

// Map controllers
app.MapControllers();

// Apply database migrations on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        logger.LogInformation("Applying database migrations for AuthService...");
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations completed for AuthService");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error applying database migrations for AuthService");
        throw;
    }
}

app.Run();
