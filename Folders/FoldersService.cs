using DataForge.Data;
using DataForge.Folders.DTOs;
using Microsoft.EntityFrameworkCore;

namespace DataForge.Folders;

public class FoldersService
{
    private readonly AppDbContext _db;

    public FoldersService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<FolderMeta>> GetAllAsync(int? parentId)
    {
        var query = _db.FolderMetas
            .Include(f => f.Children)
            .AsQueryable();

        if (parentId.HasValue)
            query = query.Where(f => f.ParentId == parentId.Value);
        else
            query = query.Where(f => f.ParentId == null);

        return await query.OrderBy(f => f.SortOrder).ThenBy(f => f.Name).ToListAsync();
    }

    public async Task<List<FolderMeta>> GetTreeAsync()
    {
        var all = await _db.FolderMetas
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Name)
            .ToListAsync();

        return BuildTree(all, null);
    }

    private static List<FolderMeta> BuildTree(List<FolderMeta> all, int? parentId)
    {
        return all
            .Where(f => f.ParentId == parentId)
            .Select(f =>
            {
                f.Children = BuildTree(all, f.Id);
                return f;
            })
            .ToList();
    }

    public async Task<FolderMeta> CreateAsync(CreateFolderDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("Folder name is required");

        var folder = new FolderMeta
        {
            Name = dto.Name.Trim(),
            ParentId = dto.ParentId,
        };

        _db.FolderMetas.Add(folder);
        await _db.SaveChangesAsync();
        return folder;
    }

    public async Task<FolderMeta> UpdateAsync(int id, UpdateFolderDto dto)
    {
        var folder = await _db.FolderMetas.FindAsync(id)
            ?? throw new KeyNotFoundException("Folder not found");

        if (dto.Name is not null)
            folder.Name = dto.Name.Trim();
        if (dto.ParentId is not null)
            folder.ParentId = dto.ParentId;
        if (dto.SortOrder is not null)
            folder.SortOrder = dto.SortOrder.Value;

        await _db.SaveChangesAsync();
        return folder;
    }

    public async Task DeleteAsync(int id)
    {
        var folder = await _db.FolderMetas
            .Include(f => f.Files)
            .FirstOrDefaultAsync(f => f.Id == id)
            ?? throw new KeyNotFoundException("Folder not found");

        var children = await _db.FolderMetas
            .Where(f => f.ParentId == id)
            .ToListAsync();
        foreach (var child in children)
            await DeleteAsync(child.Id);

        _db.FolderMetas.Remove(folder);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteBatchAsync(List<int> ids)
    {
        foreach (var id in ids)
            await DeleteAsync(id);
    }

    public async Task<List<FolderMeta>> GetAncestorsAsync(int id)
    {
        var all = await _db.FolderMetas.ToListAsync();
        var result = new List<FolderMeta>();
        var current = all.FirstOrDefault(f => f.Id == id);

        while (current != null)
        {
            result.Insert(0, current);
            current = current.ParentId.HasValue
                ? all.FirstOrDefault(f => f.Id == current.ParentId.Value)
                : null;
        }

        return result;
    }
}
