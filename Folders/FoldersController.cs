using DataForge.Common.Models;
using DataForge.Folders.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataForge.Folders;

[ApiController]
[Route("api/v1/folders")]
[Authorize]
public class FoldersController : ControllerBase
{
    private readonly FoldersService _foldersService;

    public FoldersController(FoldersService foldersService)
    {
        _foldersService = foldersService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? parentId)
    {
        var folders = await _foldersService.GetAllAsync(parentId);
        return Ok(ApiResponse<List<FolderMeta>>.Ok(folders));
    }

    [HttpGet("tree")]
    public async Task<IActionResult> GetTree()
    {
        var tree = await _foldersService.GetTreeAsync();
        return Ok(ApiResponse<List<FolderMeta>>.Ok(tree));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFolderDto dto)
    {
        try
        {
            var folder = await _foldersService.CreateAsync(dto);
            return StatusCode(201, ApiResponse<FolderMeta>.Ok(folder, "Folder created.", AppCodes.Created));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.Fail(ex.Message, AppCodes.ValidationError));
        }
    }

    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateFolderDto dto)
    {
        try
        {
            var folder = await _foldersService.UpdateAsync(id, dto);
            return Ok(ApiResponse<FolderMeta>.Ok(folder, "Folder updated."));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail("Folder not found.", AppCodes.NotFound));
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _foldersService.DeleteAsync(id);
            return Ok(ApiResponse<object>.Ok(null!, "Folder deleted."));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiResponse<object>.Fail("Folder not found.", AppCodes.NotFound));
        }
    }

    [HttpPost("delete")]
    public async Task<IActionResult> DeleteBatch([FromBody] DeleteFoldersDto dto)
    {
        await _foldersService.DeleteBatchAsync(dto.Ids);
        return Ok(ApiResponse<object>.Ok(null!, "Folders deleted."));
    }

    [HttpGet("{id:int}/ancestors")]
    public async Task<IActionResult> GetAncestors(int id)
    {
        var ancestors = await _foldersService.GetAncestorsAsync(id);
        return Ok(ApiResponse<List<FolderMeta>>.Ok(ancestors));
    }
}
