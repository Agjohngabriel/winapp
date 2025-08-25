// src/AutoConnect.Api/Controllers/VehicleSessionsController.cs
using AutoConnect.Core.Interfaces;
using AutoConnect.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace AutoConnect.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VehicleSessionsController : ControllerBase
{
    private readonly IVehicleSessionService _sessionService;
    private readonly ILogger<VehicleSessionsController> _logger;

    public VehicleSessionsController(IVehicleSessionService sessionService, ILogger<VehicleSessionsController> logger)
    {
        _sessionService = sessionService;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<VehicleSessionDto>>> GetSession(Guid id)
    {
        try
        {
            var session = await _sessionService.GetSessionByIdAsync(id);
            if (session == null)
            {
                return NotFound(ApiResponse<VehicleSessionDto>.ErrorResult("Vehicle session not found"));
            }

            var sessionDto = new VehicleSessionDto
            {
                Id = session.Id,
                ClientId = session.ClientId,
                VIN = session.VIN,
                SessionStartedAt = session.SessionStartedAt,
                SessionEndedAt = session.SessionEndedAt,
                ConnectionStatus = session.ConnectionStatus,
                ObdAdapterType = session.ObdAdapterType,
                ObdProtocol = session.ObdProtocol,
                PingLatencyMs = session.PingLatencyMs,
                DataUsageMB = session.DataUsageMB,
                LastErrorMessage = session.LastErrorMessage,
                LastDataReceivedAt = session.LastDataReceivedAt
            };

            return Ok(ApiResponse<VehicleSessionDto>.SuccessResult(sessionDto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving vehicle session {SessionId}", id);
            return StatusCode(500, ApiResponse<VehicleSessionDto>.ErrorResult("Internal server error"));
        }
    }

    [HttpGet("client/{clientId:guid}")]
    public async Task<ActionResult<ApiResponse<IEnumerable<VehicleSessionDto>>>> GetSessionsByClient(Guid clientId)
    {
        try
        {
            var sessions = await _sessionService.GetSessionsByClientIdAsync(clientId);
            var sessionDtos = sessions.Select(s => new VehicleSessionDto
            {
                Id = s.Id,
                ClientId = s.ClientId,
                VIN = s.VIN,
                SessionStartedAt = s.SessionStartedAt,
                SessionEndedAt = s.SessionEndedAt,
                ConnectionStatus = s.ConnectionStatus,
                ObdAdapterType = s.ObdAdapterType,
                ObdProtocol = s.ObdProtocol,
                PingLatencyMs = s.PingLatencyMs,
                DataUsageMB = s.DataUsageMB,
                LastErrorMessage = s.LastErrorMessage,
                LastDataReceivedAt = s.LastDataReceivedAt
            });

            return Ok(ApiResponse<IEnumerable<VehicleSessionDto>>.SuccessResult(sessionDtos));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving vehicle sessions for client {ClientId}", clientId);
            return StatusCode(500, ApiResponse<IEnumerable<VehicleSessionDto>>.ErrorResult("Internal server error"));
        }
    }

    [HttpGet("client/{clientId:guid}/active")]
    public async Task<ActionResult<ApiResponse<VehicleSessionDto>>> GetActiveSession(Guid clientId)
    {
        try
        {
            var session = await _sessionService.GetActiveSessionByClientIdAsync(clientId);
            if (session == null)
            {
                return NotFound(ApiResponse<VehicleSessionDto>.ErrorResult("No active session found for client"));
            }

            var sessionDto = new VehicleSessionDto
            {
                Id = session.Id,
                ClientId = session.ClientId,
                VIN = session.VIN,
                SessionStartedAt = session.SessionStartedAt,
                SessionEndedAt = session.SessionEndedAt,
                ConnectionStatus = session.ConnectionStatus,
                ObdAdapterType = session.ObdAdapterType,
                ObdProtocol = session.ObdProtocol,
                PingLatencyMs = session.PingLatencyMs,
                DataUsageMB = session.DataUsageMB,
                LastErrorMessage = session.LastErrorMessage,
                LastDataReceivedAt = session.LastDataReceivedAt
            };

            return Ok(ApiResponse<VehicleSessionDto>.SuccessResult(sessionDto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active session for client {ClientId}", clientId);
            return StatusCode(500, ApiResponse<VehicleSessionDto>.ErrorResult("Internal server error"));
        }
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<VehicleSessionDto>>> CreateSession([FromBody] CreateVehicleSessionRequest request)
    {
        try
        {
            var session = await _sessionService.CreateSessionAsync(
                request.ClientId,
                request.VIN,
                request.ObdAdapterType,
                request.ObdProtocol);

            var sessionDto = new VehicleSessionDto
            {
                Id = session.Id,
                ClientId = session.ClientId,
                VIN = session.VIN,
                SessionStartedAt = session.SessionStartedAt,
                SessionEndedAt = session.SessionEndedAt,
                ConnectionStatus = session.ConnectionStatus,
                ObdAdapterType = session.ObdAdapterType,
                ObdProtocol = session.ObdProtocol,
                PingLatencyMs = session.PingLatencyMs,
                DataUsageMB = session.DataUsageMB,
                LastErrorMessage = session.LastErrorMessage,
                LastDataReceivedAt = session.LastDataReceivedAt
            };

            return CreatedAtAction(nameof(GetSession), new { id = session.Id },
                ApiResponse<VehicleSessionDto>.SuccessResult(sessionDto, "Vehicle session created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<VehicleSessionDto>.ErrorResult(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating vehicle session");
            return StatusCode(500, ApiResponse<VehicleSessionDto>.ErrorResult("Internal server error"));
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<VehicleSessionDto>>> UpdateSession(Guid id, [FromBody] UpdateVehicleSessionRequest request)
    {
        try
        {
            var session = await _sessionService.UpdateSessionAsync(
                id,
                request.VIN,
                request.ConnectionStatus,
                request.ObdAdapterType,
                request.ObdProtocol,
                request.PingLatencyMs,
                request.DataUsageMB,
                request.LastErrorMessage);

            var sessionDto = new VehicleSessionDto
            {
                Id = session.Id,
                ClientId = session.ClientId,
                VIN = session.VIN,
                SessionStartedAt = session.SessionStartedAt,
                SessionEndedAt = session.SessionEndedAt,
                ConnectionStatus = session.ConnectionStatus,
                ObdAdapterType = session.ObdAdapterType,
                ObdProtocol = session.ObdProtocol,
                PingLatencyMs = session.PingLatencyMs,
                DataUsageMB = session.DataUsageMB,
                LastErrorMessage = session.LastErrorMessage,
                LastDataReceivedAt = session.LastDataReceivedAt
            };

            return Ok(ApiResponse<VehicleSessionDto>.SuccessResult(sessionDto, "Vehicle session updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<VehicleSessionDto>.ErrorResult(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating vehicle session {SessionId}", id);
            return StatusCode(500, ApiResponse<VehicleSessionDto>.ErrorResult("Internal server error"));
        }
    }

    [HttpPost("{id:guid}/end")]
    public async Task<ActionResult<ApiResponse>> EndSession(Guid id)
    {
        try
        {
            await _sessionService.EndSessionAsync(id);
            return Ok(ApiResponse.CreateSuccess("Vehicle session ended successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.CreateError(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ending vehicle session {SessionId}", id);
            return StatusCode(500, ApiResponse.CreateError("Internal server error"));
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> DeleteSession(Guid id)
    {
        try
        {
            await _sessionService.DeleteSessionAsync(id);
            return Ok(ApiResponse.CreateSuccess("Vehicle session deleted successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.CreateError(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting vehicle session {SessionId}", id);
            return StatusCode(500, ApiResponse.CreateError("Internal server error"));
        }
    }
}