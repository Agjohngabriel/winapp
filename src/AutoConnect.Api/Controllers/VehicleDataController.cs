// src/AutoConnect.Api/Controllers/VehicleDataController.cs
using AutoConnect.Core.Interfaces;
using AutoConnect.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace AutoConnect.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VehicleDataController : ControllerBase
{
    private readonly IVehicleDataService _dataService;
    private readonly ILogger<VehicleDataController> _logger;

    public VehicleDataController(IVehicleDataService dataService, ILogger<VehicleDataController> logger)
    {
        _dataService = dataService;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<VehicleDataDto>>> GetVehicleData(Guid id)
    {
        try
        {
            var data = await _dataService.GetVehicleDataByIdAsync(id);
            if (data == null)
            {
                return NotFound(ApiResponse<VehicleDataDto>.ErrorResult("Vehicle data not found"));
            }

            var dataDto = new VehicleDataDto
            {
                Id = data.Id,
                VehicleSessionId = data.VehicleSessionId,
                Timestamp = data.Timestamp,
                BatteryVoltage = data.BatteryVoltage,
                KL15Voltage = data.KL15Voltage,
                KL30Voltage = data.KL30Voltage,
                IgnitionStatus = data.IgnitionStatus,
                EngineRPM = data.EngineRPM,
                VehicleSpeed = data.VehicleSpeed,
                CoolantTemperature = data.CoolantTemperature,
                FuelLevel = data.FuelLevel,
                DiagnosticTroubleCodes = data.DiagnosticTroubleCodes
            };

            return Ok(ApiResponse<VehicleDataDto>.SuccessResult(dataDto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving vehicle data {DataId}", id);
            return StatusCode(500, ApiResponse<VehicleDataDto>.ErrorResult("Internal server error"));
        }
    }

    [HttpGet("session/{sessionId:guid}")]
    public async Task<ActionResult<ApiResponse<IEnumerable<VehicleDataDto>>>> GetDataBySession(Guid sessionId)
    {
        try
        {
            var dataPoints = await _dataService.GetDataBySessionIdAsync(sessionId);
            var dataDtos = dataPoints.Select(d => new VehicleDataDto
            {
                Id = d.Id,
                VehicleSessionId = d.VehicleSessionId,
                Timestamp = d.Timestamp,
                BatteryVoltage = d.BatteryVoltage,
                KL15Voltage = d.KL15Voltage,
                KL30Voltage = d.KL30Voltage,
                IgnitionStatus = d.IgnitionStatus,
                EngineRPM = d.EngineRPM,
                VehicleSpeed = d.VehicleSpeed,
                CoolantTemperature = d.CoolantTemperature,
                FuelLevel = d.FuelLevel,
                DiagnosticTroubleCodes = d.DiagnosticTroubleCodes
            });

            return Ok(ApiResponse<IEnumerable<VehicleDataDto>>.SuccessResult(dataDtos));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving vehicle data for session {SessionId}", sessionId);
            return StatusCode(500, ApiResponse<IEnumerable<VehicleDataDto>>.ErrorResult("Internal server error"));
        }
    }

    [HttpGet("session/{sessionId:guid}/latest")]
    public async Task<ActionResult<ApiResponse<VehicleDataDto>>> GetLatestData(Guid sessionId)
    {
        try
        {
            var data = await _dataService.GetLatestDataBySessionIdAsync(sessionId);
            if (data == null)
            {
                return NotFound(ApiResponse<VehicleDataDto>.ErrorResult("No vehicle data found for session"));
            }

            var dataDto = new VehicleDataDto
            {
                Id = data.Id,
                VehicleSessionId = data.VehicleSessionId,
                Timestamp = data.Timestamp,
                BatteryVoltage = data.BatteryVoltage,
                KL15Voltage = data.KL15Voltage,
                KL30Voltage = data.KL30Voltage,
                IgnitionStatus = data.IgnitionStatus,
                EngineRPM = data.EngineRPM,
                VehicleSpeed = data.VehicleSpeed,
                CoolantTemperature = data.CoolantTemperature,
                FuelLevel = data.FuelLevel,
                DiagnosticTroubleCodes = data.DiagnosticTroubleCodes
            };

            return Ok(ApiResponse<VehicleDataDto>.SuccessResult(dataDto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving latest vehicle data for session {SessionId}", sessionId);
            return StatusCode(500, ApiResponse<VehicleDataDto>.ErrorResult("Internal server error"));
        }
    }

    [HttpGet("session/{sessionId:guid}/recent")]
    public async Task<ActionResult<ApiResponse<IEnumerable<VehicleDataDto>>>> GetRecentData(Guid sessionId, [FromQuery] int count = 50)
    {
        try
        {
            var dataPoints = await _dataService.GetRecentDataBySessionIdAsync(sessionId, count);
            var dataDtos = dataPoints.Select(d => new VehicleDataDto
            {
                Id = d.Id,
                VehicleSessionId = d.VehicleSessionId,
                Timestamp = d.Timestamp,
                BatteryVoltage = d.BatteryVoltage,
                KL15Voltage = d.KL15Voltage,
                KL30Voltage = d.KL30Voltage,
                IgnitionStatus = d.IgnitionStatus,
                EngineRPM = d.EngineRPM,
                VehicleSpeed = d.VehicleSpeed,
                CoolantTemperature = d.CoolantTemperature,
                FuelLevel = d.FuelLevel,
                DiagnosticTroubleCodes = d.DiagnosticTroubleCodes
            });

            return Ok(ApiResponse<IEnumerable<VehicleDataDto>>.SuccessResult(dataDtos));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving recent vehicle data for session {SessionId}", sessionId);
            return StatusCode(500, ApiResponse<IEnumerable<VehicleDataDto>>.ErrorResult("Internal server error"));
        }
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<VehicleDataDto>>> CreateVehicleData([FromBody] CreateVehicleDataRequest request)
    {
        try
        {
            var data = await _dataService.CreateVehicleDataAsync(
                request.VehicleSessionId,
                request.BatteryVoltage,
                request.KL15Voltage,
                request.KL30Voltage,
                request.IgnitionStatus,
                request.EngineRPM,
                request.VehicleSpeed,
                request.CoolantTemperature,
                request.FuelLevel,
                request.DiagnosticTroubleCodes,
                request.RawObdData);

            var dataDto = new VehicleDataDto
            {
                Id = data.Id,
                VehicleSessionId = data.VehicleSessionId,
                Timestamp = data.Timestamp,
                BatteryVoltage = data.BatteryVoltage,
                KL15Voltage = data.KL15Voltage,
                KL30Voltage = data.KL30Voltage,
                IgnitionStatus = data.IgnitionStatus,
                EngineRPM = data.EngineRPM,
                VehicleSpeed = data.VehicleSpeed,
                CoolantTemperature = data.CoolantTemperature,
                FuelLevel = data.FuelLevel,
                DiagnosticTroubleCodes = data.DiagnosticTroubleCodes
            };

            return CreatedAtAction(nameof(GetVehicleData), new { id = data.Id },
                ApiResponse<VehicleDataDto>.SuccessResult(dataDto, "Vehicle data created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<VehicleDataDto>.ErrorResult(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating vehicle data");
            return StatusCode(500, ApiResponse<VehicleDataDto>.ErrorResult("Internal server error"));
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> DeleteVehicleData(Guid id)
    {
        try
        {
            await _dataService.DeleteVehicleDataAsync(id);
            return Ok(ApiResponse.Success("Vehicle data deleted successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Error(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting vehicle data {DataId}", id);
            return StatusCode(500, ApiResponse.Error("Internal server error"));
        }
    }
}