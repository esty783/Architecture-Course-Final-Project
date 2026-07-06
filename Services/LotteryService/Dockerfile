# Multi-stage build for LotteryService
# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src

# Copy solution and project files
COPY ["Services/LotteryService/LotteryService.csproj", "Services/LotteryService/"]
COPY ["Shared/SharedModels/SharedModels.csproj", "Shared/SharedModels/"]

# Restore dependencies
RUN dotnet restore "Services/LotteryService/LotteryService.csproj"

# Copy all source code
COPY ["Services/LotteryService/", "Services/LotteryService/"]
COPY ["Shared/SharedModels/", "Shared/SharedModels/"]

# Build and publish
WORKDIR "/src/Services/LotteryService"
RUN dotnet publish "LotteryService.csproj" -c Release -o /app/publish

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app

# Copy published application
COPY --from=build /app/publish .

# Expose port
EXPOSE 5004

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:5004/health || exit 1

# Set environment variables defaults
ENV ASPNETCORE_URLS=http://+:5004
ENV ASPNETCORE_ENVIRONMENT=Production

# Run the application
ENTRYPOINT ["dotnet", "LotteryService.dll"]
