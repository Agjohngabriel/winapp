// src/AutoConnect.Api/Controllers/ClientsController.cs
using AutoConnect.Core.Interfaces;
using AutoConnect.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace AutoConnect.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClientsController : ControllerBase
{
    private readonly IClientService _clientService;
    private readonly ILogger<ClientsController> _logger;

    public ClientsController(IClientService clientService, ILogger<ClientsController> logger)
    {
        _clientService = clientService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<ClientDto>>>> GetClients()
    {
        try
        {
            var clients = await _clientService.GetAllClientsAsync();
            var clientDtos = clients.Select(c => new ClientDto
            {
                Id = c.Id,
                Name = c.Name,
                Email = c.Email,
                LastKnownIpAddress = c.LastKnownIpAddress,
                LastConnectedAt = c.LastConnectedAt,
                Status = c.Status,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            });

            return Ok(ApiResponse<IEnumerable<ClientDto>>.SuccessResult(clientDtos));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving clients");
            return StatusCode(500, ApiResponse<IEnumerable<ClientDto>>.ErrorResult("Internal server error"));
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ClientDto>>> GetClient(Guid id)
    {
        try
        {
            var client = await _clientService.GetClientByIdAsync(id);
            if (client == null)
            {
                return NotFound(ApiResponse<ClientDto>.ErrorResult("Client not found"));
            }

            var clientDto = new ClientDto
            {
                Id = client.Id,
                Name = client.Name,
                Email = client.Email,
                LastKnownIpAddress = client.LastKnownIpAddress,
                LastConnectedAt = client.LastConnectedAt,
                Status = client.Status,
                CreatedAt = client.CreatedAt,
                UpdatedAt = client.UpdatedAt
            };

            return Ok(ApiResponse<ClientDto>.SuccessResult(clientDto));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving client {ClientId}", id);
            return StatusCode(500, ApiResponse<ClientDto>.ErrorResult("Internal server error"));
        }
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ClientDto>>> CreateClient([FromBody] CreateClientRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(ApiResponse<ClientDto>.ErrorResult("Name and Email are required"));
            }

            var client = await _clientService.CreateClientAsync(request.Name, request.Email, request.Notes);

            var clientDto = new ClientDto
            {
                Id = client.Id,
                Name = client.Name,
                Email = client.Email,
                LastKnownIpAddress = client.LastKnownIpAddress,
                LastConnectedAt = client.LastConnectedAt,
                Status = client.Status,
                CreatedAt = client.CreatedAt,
                UpdatedAt = client.UpdatedAt
            };

            return CreatedAtAction(nameof(GetClient), new { id = client.Id },
                ApiResponse<ClientDto>.SuccessResult(clientDto, "Client created successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ClientDto>.ErrorResult(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating client");
            return StatusCode(500, ApiResponse<ClientDto>.ErrorResult("Internal server error"));
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ClientDto>>> UpdateClient(Guid id, [FromBody] UpdateClientRequest request)
    {
        try
        {
            var client = await _clientService.UpdateClientAsync(id, request.Name, request.Email, request.Status, request.Notes);

            var clientDto = new ClientDto
            {
                Id = client.Id,
                Name = client.Name,
                Email = client.Email,
                LastKnownIpAddress = client.LastKnownIpAddress,
                LastConnectedAt = client.LastConnectedAt,
                Status = client.Status,
                CreatedAt = client.CreatedAt,
                UpdatedAt = client.UpdatedAt
            };

            return Ok(ApiResponse<ClientDto>.SuccessResult(clientDto, "Client updated successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<ClientDto>.ErrorResult(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating client {ClientId}", id);
            return StatusCode(500, ApiResponse<ClientDto>.ErrorResult("Internal server error"));
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse>> DeleteClient(Guid id)
    {
        try
        {
            await _clientService.DeleteClientAsync(id);
            return Ok(ApiResponse.Success("Client deleted successfully"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse.Error(ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting client {ClientId}", id);
            return StatusCode(500, ApiResponse.Error("Internal server error"));
        }
    }
}