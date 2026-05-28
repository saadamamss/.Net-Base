using System.Text.Json.Serialization;
using DataForge.Folders;

namespace DataForge.Upload;

public class FileMeta
{
    public Guid Id { get; set; }
    public string FilenameDisk { get; set; } = string.Empty;
    public string FilenameDownload { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Type { get; set; } = string.Empty;
    public long Filesize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public Guid? UploadedBy { get; set; }
    public DateTime UploadedOn { get; set; } = DateTime.UtcNow;
    public int? FolderId { get; set; }

    [JsonIgnore]
    public FolderMeta? Folder { get; set; }
}
