using DataForge.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataForge.Upload;

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
        var (success, message, data) = await _uploadService.UploadImageAsync(file, Request);

        if (!success)
            return BadRequest(ApiResponse<object>.Fail(message, AppCodes.ValidationError));

        return Ok(ApiResponse<object>.Ok(data!, message));
    }

    [HttpPost("file")]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        var (success, message, data) = await _uploadService.UploadFileAsync(file, Request);

        if (!success)
            return BadRequest(ApiResponse<object>.Fail(message, AppCodes.ValidationError));

        return Ok(ApiResponse<object>.Ok(data, message));
    }

    [HttpGet("files/{id:guid}")]
    public async Task<IActionResult> GetFileInfo(Guid id)
    {
        var data = await _uploadService.GetFileInfoAsync(id, Request);
        if (data == null)
            return NotFound(ApiResponse<object>.Fail("File not found.", AppCodes.NotFound));
        return Ok(ApiResponse<object>.Ok(data));
    }

    [HttpGet("files")]
    public async Task<IActionResult> ListFiles([FromQuery] int? folderId)
    {
        var files = await _uploadService.GetFilesAsync(folderId, Request);
        return Ok(ApiResponse<object>.Ok(files));
    }

    [HttpPatch("files/move")]
    public async Task<IActionResult> MoveFiles([FromBody] MoveFilesDto dto)
    {
        await _uploadService.MoveFilesAsync(dto.FileIds, dto.FolderId);
        return Ok(ApiResponse<object>.Ok(null!, "Files moved."));
    }

    [HttpPost("files/delete")]
    public async Task<IActionResult> DeleteFiles([FromBody] DeleteFilesDto dto)
    {
        await _uploadService.DeleteFilesAsync(dto.FileIds);
        return Ok(ApiResponse<object>.Ok(null!, "Files deleted."));
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

public class MoveFilesDto
{
    public List<Guid> FileIds { get; set; } = [];
    public int? FolderId { get; set; }
}

public class DeleteFilesDto
{
    public List<Guid> FileIds { get; set; } = [];
}
