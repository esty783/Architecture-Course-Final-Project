using Serilog;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using ApiGateway.Middleware;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Ensure logs directory exists
var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
if (!Directory.Exists(logDirectory))
{
    Directory.CreateDirectory(logDirectory);
}

// Add configuration from ocelot.json
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);

// Add Serilog for logging
var seqUrl = builder.Configuration["Seq:ServerUrl"] ?? "http://seq:5341";
builder.Host.UseSerilog((context, config) =>
{
    config
        .MinimumLevel.Information()
        .WriteTo.Console()
        .WriteTo.File(
            Path.Combine(logDirectory, "api-gateway-.txt"),
            rollingInterval: RollingInterval.Day,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.Seq(seqUrl)
        .Enrich.FromLogContext();
});

// Add Ocelot
builder.Services.AddOcelot(builder.Configuration);

// Add Controllers + HttpClient for BFF
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ApiGateway API",
        Version = "v1",
        Description = "API Gateway endpoints"
    });
});

// Add Authentication with JWT Bearer
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        var secretKey = builder.Configuration["Jwt:SecretKey"] 
            ?? throw new InvalidOperationException("JWT:SecretKey not configured in appsettings");
        
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(secretKey)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "AuthService",
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// Add CORS for Angular app
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseSerilogRequestLogging();
app.UseCors("AllowAngularApp");

// Correlation ID middleware — generates/forwards X-Correlation-ID for all requests
app.Use(async (context, next) =>
{
    const string header = "X-Correlation-ID";
    var correlationId = context.Request.Headers.TryGetValue(header, out var existing)
        ? existing.ToString()
        : Guid.NewGuid().ToString();

    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers.Append(header, correlationId);
    // Ensure downstream services receive the header
    context.Request.Headers[header] = correlationId;

    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
    {
        await next();
    }
});

// Add custom JWT validation middleware
app.UseMiddleware<JwtValidationMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

// Swagger must run before Ocelot so /swagger isn't routed downstream
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "ApiGateway API v1");
    options.RoutePrefix = "swagger";
});

// Map BFF controllers (before Ocelot so they take priority)
app.MapControllers();

// Use Ocelot for routing
await app.UseOcelot();

app.Run("http://+:5000");
