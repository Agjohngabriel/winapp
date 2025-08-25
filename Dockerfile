# Use the official .NET 8 SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution file
COPY AutoConnect.sln ./

# Copy project files
COPY src/AutoConnect.Api/AutoConnect.Api.csproj src/AutoConnect.Api/
COPY src/AutoConnect.Core/AutoConnect.Core.csproj src/AutoConnect.Core/
COPY src/AutoConnect.Infrastructure/AutoConnect.Infrastructure.csproj src/AutoConnect.Infrastructure/
COPY src/AutoConnect.Shared/AutoConnect.Shared.csproj src/AutoConnect.Shared/

# Restore dependencies
RUN dotnet restore

# Copy source code
COPY src/ src/

# Build the application
WORKDIR /src/src/AutoConnect.Api
RUN dotnet build -c Release -o /app/build

# Publish the application
RUN dotnet publish -c Release -o /app/publish --no-restore

# Use the official .NET 8 runtime image for running
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=build /app/publish .

# Create a non-root user
RUN groupadd -r appuser && useradd -r -g appuser appuser
RUN chown -R appuser:appuser /app
USER appuser

# Expose ports
EXPOSE 5000
EXPOSE 5001

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:5000/health || exit 1

# Set environment variables
ENV ASPNETCORE_URLS=http://+:5000;https://+:5001
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "AutoConnect.Api.dll"]