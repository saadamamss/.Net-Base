using DataForge.Collections.DTOs;
using DataForge.Common.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataForge.Collections;

[ApiController]
[Route("api/v1/collections")]
[Authorize]
public class CollectionsController : ControllerBase
{
    private readonly CollectionService _collectionsService;

    public CollectionsController(CollectionService collectionsService)
    {
        _collectionsService = collectionsService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateCollectionDto dto)
    {
        var collection = await _collectionsService.CreateAsync(dto);
        return StatusCode(201, ApiResponse<CollectionResponseDto>.Ok(collection, "Collection created.", AppCodes.Created));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var collections = await _collectionsService.GetAllListAsync();
        return Ok(ApiResponse<List<CollectionResponseDto>>.Ok(collections));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var collection = await _collectionsService.GetByIdAsync(id);
        return Ok(ApiResponse<CollectionResponseDto>.Ok(collection));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _collectionsService.DeleteAsync(id);
        return Ok(ApiResponse<object>.Ok(null!, "Collection deleted."));
    }
}
