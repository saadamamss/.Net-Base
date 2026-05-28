using DataForge.Common.Models;
using DataForge.Fields.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DataForge.Fields;

[ApiController]
[Route("api/v1/collections/{collectionId}/fields")]
[Authorize]
public class FieldsController : ControllerBase
{
    private readonly FieldsService _fieldsService;

    public FieldsController(FieldsService fieldsService)
    {
        _fieldsService = fieldsService;
    }

    [HttpPost]
    public async Task<IActionResult> Create(int collectionId, CreateFieldDto dto)
    {
        var field = await _fieldsService.CreateAsync(collectionId, dto);
        return StatusCode(201, ApiResponse<FieldResponseDto>.Ok(field, "Field created.", AppCodes.Created));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(int collectionId)
    {
        var fields = await _fieldsService.GetAllAsync(collectionId);
        return Ok(ApiResponse<List<FieldResponseDto>>.Ok(fields));
    }

    [HttpGet("~/api/v1/fields")]
    public async Task<IActionResult> GetAllFields()
    {
        var fields = await _fieldsService.GetAllFieldsAsync();
        return Ok(ApiResponse<List<FieldResponseDto>>.Ok(fields));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int collectionId, int id)
    {
        var field = await _fieldsService.GetByIdAsync(collectionId, id);
        return Ok(ApiResponse<FieldResponseDto>.Ok(field));
    }

    [HttpPatch("sort/reorder")]
    public async Task<IActionResult> Reorder(int collectionId, List<ReorderFieldDto> items)
    {
        await _fieldsService.ReorderAsync(collectionId, items);
        return Ok(ApiResponse<object>.Ok(null!, "Fields reordered."));
    }

    [HttpPatch("{id}")]
    public async Task<IActionResult> Update(int collectionId, int id, UpdateFieldDto dto)
    {
        var field = await _fieldsService.UpdateAsync(collectionId, id, dto);
        return Ok(ApiResponse<FieldResponseDto>.Ok(field, "Field updated."));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int collectionId, int id)
    {
        await _fieldsService.DeleteAsync(collectionId, id);
        return Ok(ApiResponse<object>.Ok(null!, "Field deleted."));
    }
}
