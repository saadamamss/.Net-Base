using DotnetStarterKit.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotnetStarterKit.Upload;

[ApiController]
[Route("api/v1/upload")]
[Authorize]
public class UploadController : ControllerBase
{
    private readonly UploadService _uploadService;

    public UploadController(UploadService uploadService)
    {
        _uploadService = uploadService;
    }

    [HttpPost("image")]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        var (success, message, url) = await _uploadService.UploadImageAsync(file, Request);

        if (!success)
            return BadRequest(ApiResponse<object>.Fail(message, AppCodes.ValidationError));

        return Ok(ApiResponse<object>.Ok(new { url }, message));
    }

    [HttpPost("file")]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        var (success, message, url) = await _uploadService.UploadFileAsync(file, Request);

        if (!success)
            return BadRequest(ApiResponse<object>.Fail(message, AppCodes.ValidationError));

        return Ok(ApiResponse<object>.Ok(new { url }, message));
    }

    [HttpDelete("{filename}")]
    [Authorize(Roles = "Admin")]
    public IActionResult Delete(string filename)
    {
        var (success, message) = _uploadService.DeleteFile(filename);

        if (!success)
            return NotFound(ApiResponse<object>.Fail(message, AppCodes.NotFound));

        return Ok(ApiResponse<object>.Ok(null!, message));
    }
}