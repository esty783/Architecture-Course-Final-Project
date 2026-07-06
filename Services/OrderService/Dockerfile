# Multi-stage build for OrderService
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

# Copy solution and project files
COPY ["Services/OrderService/OrderService.csproj", "Services/OrderService/"]
COPY ["Shared/SharedModels/SharedModels.csproj", "Shared/SharedModels/"]

# Restore dependencies
RUN dotnet restore "Services/OrderService/OrderService.csproj"

# Copy all source code
COPY ["Services/OrderService/", "Services/OrderService/"]
COPY ["Shared/SharedModels/", "Shared/SharedModels/"]

# Build and publish
WORKDIR "/src/Services/OrderService"
RUN dotnet publish "OrderService.csproj" -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app

# Copy published application
COPY --from=build /app/publish .

# Expose port
EXPOSE 5003

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:5003/health || exit 1

# Set environment variables defaults
ENV ASPNETCORE_URLS=http://+:5003
ENV ASPNETCORE_ENVIRONMENT=Production

# Run the application
ENTRYPOINT ["dotnet", "OrderService.dll"]
