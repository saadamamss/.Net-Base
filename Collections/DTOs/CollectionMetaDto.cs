namespace DataForge.Collections.DTOs;

public class CollectionMetaDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public bool Singleton { get; set; }
    public string PrimaryKey { get; set; } = "id";
    public string PkType { get; set; } = "auto-increment";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
