using DataForge.Fields;

namespace DataForge.Collections;

public class CollectionMeta
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public bool Singleton { get; set; } = false;
    public string PrimaryKey { get; set; } = "id";
    public string PkType { get; set; } = "auto-increment";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<FieldMeta> Fields { get; set; } = [];
}
