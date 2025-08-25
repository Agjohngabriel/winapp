// src/AutoConnect.Infrastructure/Services/VehicleSessionService.cs
using AutoConnect.Core.Entities;
using AutoConnect.Core.Enums;
using AutoConnect.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AutoConnect.Infrastructure.Services;

public class VehicleSessionService : IVehicleSessionService
{
    private readonly IVehicleSessionRepository _sessionRepository;
    private readonly IClientRepository _clientRepository;
    private readonly ILogger<VehicleSessionService> _logger;

    public VehicleSessionService(
        IVehicleSessionRepository sessionRepository,
        IClientRepository clientRepository,
        ILogger<VehicleSessionService> logger)
    {
        _sessionRepository = sessionRepository;
        _clientRepository = clientRepository;
        _logger = logger;
    }

    public async Task<VehicleSession?> GetSessionByIdAsync(Guid id)
    {
        return await _sessionRepository.GetByIdAsync(id);
    }

    public async Task<IEnumerable<VehicleSession>> GetSessionsByClientIdAsync(Guid clientId)
    {
        return await _sessionRepository.GetByClientIdAsync(clientId);
    }

    public async Task<VehicleSession?> GetActiveSessionByClientIdAsync(Guid clientId)
    {
        return await _sessionRepository.GetActiveSessionByClientIdAsync(clientId);
    }

    public async Task<VehicleSession> CreateSessionAsync(Guid clientId, string? vin = null, string? obdAdapterType = null, string? obdProtocol = null)
    {
        // Verify client exists
        if (!await _clientRepository.ExistsAsync(clientId))
        {
            throw new InvalidOperationException($"Client with ID '{clientId}' not found.");
        }

        // End any existing active session
        var activeSession = await GetActiveSessionByClientIdAsync(clientId);
        if (activeSession != null)
        {
            await EndSessionAsync(activeSession.Id);
            _logger.LogInformation("Ended previous active session {SessionId} for client {ClientId}", activeSession.Id, clientId);
        }

        var session = new VehicleSession
        {
            ClientId = clientId,
            VIN = vin,
            ObdAdapterType = obdAdapterType,
            ObdProtocol = obdProtocol,
            ConnectionStatus = VehicleConnectionStatus.Connecting,
            SessionStartedAt = DateTime.UtcNow
        };

        var createdSession = await _sessionRepository.CreateAsync(session);
        _logger.LogInformation("Created new vehicle session: {SessionId} for client {ClientId}", createdSession.Id, clientId);

        return createdSession;
    }

    public async Task<VehicleSession> UpdateSessionAsync(Guid id, string? vin = null, VehicleConnectionStatus? status = null, string? obdAdapterType = null, string? obdProtocol = null, int? pingLatency = null, decimal? dataUsage = null, string? errorMessage = null)
    {
        var session = await _sessionRepository.GetByIdAsync(id);
        if (session == null)
        {
            throw new InvalidOperationException($"Vehicle session with ID '{id}' not found.");
        }

        if (vin != null) session.VIN = vin;
        if (status.HasValue) session.ConnectionStatus = status.Value;
        if (obdAdapterType != null) session.ObdAdapterType = obdAdapterType;
        if (obdProtocol != null) session.ObdProtocol = obdProtocol;
        if (pingLatency.HasValue) session.PingLatencyMs = pingLatency.Value;
        if (dataUsage.HasValue) session.DataUsageMB = dataUsage.Value;
        if (errorMessage != null) session.LastErrorMessage = errorMessage;

        session.LastDataReceivedAt = DateTime.UtcNow;

        var updatedSession = await _sessionRepository.UpdateAsync(session);
        _logger.LogInformation("Updated vehicle session: {SessionId}", updatedSession.Id);

        return updatedSession;
    }

    public async Task EndSessionAsync(Guid id)
    {
        var session = await _sessionRepository.GetByIdAsync(id);
        if (session == null)
        {
            throw new InvalidOperationException($"Vehicle session with ID '{id}' not found.");
        }

        session.SessionEndedAt = DateTime.UtcNow;
        session.ConnectionStatus = VehicleConnectionStatus.Disconnected;

        await _sessionRepository.UpdateAsync(session);
        _logger.LogInformation("Ended vehicle session: {SessionId}", id);
    }

    public async Task DeleteSessionAsync(Guid id)
    {
        if (!await SessionExistsAsync(id))
        {
            throw new InvalidOperationException($"Vehicle session with ID '{id}' not found.");
        }

        await _sessionRepository.DeleteAsync(id);
        _logger.LogInformation("Deleted vehicle session: {SessionId}", id);
    }

    public async Task<bool> SessionExistsAsync(Guid id)
    {
        return await _sessionRepository.ExistsAsync(id);
    }
}
