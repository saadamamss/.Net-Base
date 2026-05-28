namespace DataForge.Folders.DTOs;

public class CreateFolderDto
{
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
}

public class UpdateFolderDto
{
    public string? Name { get; set; }
    public int? ParentId { get; set; }
    public int? SortOrder { get; set; }
}

public class DeleteFoldersDto
{
    public List<int> Ids { get; set; } = [];
}
