// src/AutoConnect.Infrastructure/Configuration/ServiceConfiguration.cs
using AutoConnect.Core.Interfaces;
using AutoConnect.Infrastructure.Repositories;
using AutoConnect.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AutoConnect.Infrastructure.Configuration;

public static class ServiceConfiguration
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Register repositories
        services.AddScoped<IClientRepository, ClientRepository>();
        services.AddScoped<IVehicleSessionRepository, VehicleSessionRepository>();
        services.AddScoped<IVehicleDataRepository, VehicleDataRepository>();

        // Register services
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IVehicleSessionService, VehicleSessionService>();
        services.AddScoped<IVehicleDataService, VehicleDataService>();

        return services;
    }
}