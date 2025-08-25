// src/AutoConnect.Api/Controllers/HealthController.cs
using Microsoft.AspNetCore.Mvc;
using AutoConnect.Shared.DTOs;

namespace AutoConnect.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse> GetHealth()
    {
        return Ok(ApiResponse.Success("AutoConnect API is healthy"));
    }
}