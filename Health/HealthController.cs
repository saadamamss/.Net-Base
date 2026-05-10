using DotnetStarterKit.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotnetStarterKit.Health;

[ApiController]
[Route("api/v1/health")]
public class HealthController : ControllerBase
{
    private readonly AppDbContext _db;

    public HealthController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var dbStatus = "disconnected";

        try
        {
            await _db.Database.ExecuteSqlRawAsync("SELECT 1");
            dbStatus = "connected";
        }
        catch
        {
            dbStatus = "disconnected";
        }

        return Ok(new
        {
            status = "healthy",
            database = dbStatus,
            timestamp = DateTime.UtcNow,
            version = "1.0.0"
        });
    }
}