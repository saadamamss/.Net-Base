namespace DataForge.Items;

using DataForge.Collections;
using DataForge.Common.Models;
using DataForge.Data;
using DataForge.Items.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/v1/items")]
public class ItemsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly ItemsService _itemsService;

    public ItemsController(AppDbContext dbContext, ItemsService itemsService)
    {
        _dbContext = dbContext;
        _itemsService = itemsService;
    }

    [HttpGet("{collection}")]
    public async Task<IActionResult> FindMany(
        [FromRoute] string collection,
        [FromQuery] QueryItemsDto query)
    {
        var collectionMeta = await ResolveCollection(collection);
        var items = await _itemsService.FindManyAsync(
            collectionMeta.TableName, query, collectionMeta.Fields.ToArray());
        return Ok(ApiResponse<object>.Ok(items));
    }

    [HttpGet("{collection}/{id}")]
    public async Task<IActionResult> FindOne(
        [FromRoute] string collection,
        [FromRoute] string id,
        [FromQuery] QueryItemsDto query)
    {
        var collectionMeta = await ResolveCollection(collection);
        var result = await _itemsService.FindOneAsync(
            collectionMeta.TableName, id, query.Fields, collectionMeta.Fields.ToArray());
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpPost("{collection}")]
    public async Task<IActionResult> Create(
        [FromRoute] string collection,
        [FromBody] Dictionary<string, object?> body)
    {
        var collectionMeta = await ResolveCollection(collection);
        var result = await _itemsService.CreateAsync(
            collectionMeta.TableName,
            collectionMeta.Fields.ToArray(),
            body);
        return StatusCode(201, ApiResponse<Dictionary<string, object?>>.Ok(result, "Item created.", AppCodes.Created));
    }

    [HttpPatch("{collection}/{id}")]
    public async Task<IActionResult> Update(
        [FromRoute] string collection,
        [FromRoute] string id,
        [FromBody] Dictionary<string, object?> body)
    {
        var collectionMeta = await ResolveCollection(collection);
        var result = await _itemsService.UpdateAsync(
            collectionMeta.TableName,
            id,
            collectionMeta.Fields.ToArray(),
            body);
        return Ok(ApiResponse<Dictionary<string, object?>>.Ok(result, "Item updated."));
    }

    [HttpDelete("{collection}")]
    public async Task<IActionResult> BulkRemove(
        [FromRoute] string collection,
        [FromBody] BulkDeleteDto dto)
    {
        var collectionMeta = await ResolveCollection(collection);
        await _itemsService.RemoveManyAsync(collectionMeta.TableName, dto.Ids);
        return Ok(ApiResponse<object>.Ok(null!, $"{dto.Ids.Count} items deleted."));
    }

    [HttpDelete("{collection}/{id}")]
    public async Task<IActionResult> Remove(
        [FromRoute] string collection,
        [FromRoute] string id)
    {
        var collectionMeta = await ResolveCollection(collection);
        await _itemsService.RemoveAsync(collectionMeta.TableName, id);
        return Ok(ApiResponse<object>.Ok(null!, "Item deleted."));
    }

    private async Task<CollectionMeta> ResolveCollection(string collectionName)
    {
        var collection = await _dbContext.CollectionMetas
            .Include(c => c.Fields.OrderBy(f => f.SortOrder))
            .FirstOrDefaultAsync(c => c.Name == collectionName);

        if (collection is null)
            throw new KeyNotFoundException($"Collection \"{collectionName}\" not found.");

        return collection;
    }
}
