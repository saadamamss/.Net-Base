using DataForge.Common.Models;
using DataForge.Relations.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataForge.Relations;

[ApiController]
[Route("api/v1/relations")]
[Authorize]
public class RelationsController : ControllerBase
{
    private readonly RelationsService _relationsService;
    public RelationsController(RelationsService relationsService)
    {
        _relationsService = relationsService;
    }

    [HttpGet]
    public async Task<ActionResult> GetAll()
    {
        var relations = await _relationsService.GetAllAsync();
        return Ok(ApiResponse<List<RelationResponseDto>>.Ok(relations));
    }

    [HttpGet("{collectionId:int}")]
    public async Task<ActionResult> GetByCollection(int collectionId)
    {
        var relations = await _relationsService.GetByCollectionAsync(collectionId);
        return Ok(ApiResponse<List<RelationResponseDto>>.Ok(relations));
    }
}
