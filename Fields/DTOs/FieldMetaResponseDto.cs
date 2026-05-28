namespace DataForge.Fields.DTOs;

public class FieldMetaResponseDto
{
    public int Id { get; set; }
    public int CollectionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Required { get; set; }
    public int SortOrder { get; set; }
    public bool Hidden { get; set; }
    public bool Readonly { get; set; }
    public bool Searchable { get; set; }
    public string Width { get; set; } = "full";
    public string? Note { get; set; }
    public string? Interface { get; set; }
    public string? Special { get; set; }
    public object? Options { get; set; }
    public bool IsSystem { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
