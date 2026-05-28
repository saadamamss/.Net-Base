using System.Text.Json.Serialization;
using DataForge.Upload;

namespace DataForge.Folders;

public class FolderMeta
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public int SortOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public FolderMeta? Parent { get; set; }
    public ICollection<FolderMeta> Children { get; set; } = new List<FolderMeta>();
    [JsonIgnore]
    public ICollection<FileMeta> Files { get; set; } = new List<FileMeta>();
}
