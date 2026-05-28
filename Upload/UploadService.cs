using DataForge.Data;
using Microsoft.EntityFrameworkCore;

namespace DataForge.Upload;

public class UploadService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<UploadService> _logger;
    private readonly AppDbContext _db;

    private static readonly string[] AllowedImageTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];
    private const long MaxImageSize = 5 * 1024 * 1024; // 5MB
    private const long MaxFileSize = 10 * 1024 * 1024; // 10MB

    public UploadService(IWebHostEnvironment env, ILogger<UploadService> logger, AppDbContext db)
    {
        _env = env;
        _logger = logger;
        _db = db;
    }

    // ── Upload Image ───────────────────────────────────────────
    public async Task<(bool Success, string Message, object? Data)> UploadImageAsync(IFormFile file, HttpRequest request)
    {
        if (file.Length == 0)
            return (false, "No file provided.", null);

        if (file.Length > MaxImageSize)
            return (false, "Image must be less than 5MB.", null);

        if (!AllowedImageTypes.Contains(file.ContentType.ToLower()))
            return (false, "Only JPEG, PNG, WEBP, and GIF images are allowed.", null);

        var fileId = Guid.NewGuid();
        var (filenameDisk, storagePath, url) = await SaveFileAsync(file, fileId, "images", request);

        var fileMeta = new FileMeta
        {
            Id = fileId,
            FilenameDisk = filenameDisk,
            FilenameDownload = file.FileName,
            Type = file.ContentType,
            Filesize = file.Length,
        };

        _db.FileMetas.Add(fileMeta);
        await _db.SaveChangesAsync();

        return (true, "Image uploaded successfully.", new
        {
            id = fileMeta.Id,
            filename_disk = fileMeta.FilenameDisk,
            filename_download = fileMeta.FilenameDownload,
            type = fileMeta.Type,
            filesize = fileMeta.Filesize,
            width = fileMeta.Width,
            height = fileMeta.Height,
            url,
        });
    }

    // ── Upload File ────────────────────────────────────────────
    public async Task<(bool Success, string Message, object? Data)> UploadFileAsync(IFormFile file, HttpRequest request)
    {
        if (file.Length == 0)
            return (false, "No file provided.", null);

        if (file.Length > MaxFileSize)
            return (false, "File must be less than 10MB.", null);

        var fileId = Guid.NewGuid();
        var (filenameDisk, _, url) = await SaveFileAsync(file, fileId, "files", request);

        var fileMeta = new FileMeta
        {
            Id = fileId,
            FilenameDisk = filenameDisk,
            FilenameDownload = file.FileName,
            Type = file.ContentType,
            Filesize = file.Length,
        };

        _db.FileMetas.Add(fileMeta);
        await _db.SaveChangesAsync();

        return (true, "File uploaded successfully.", new
        {
            id = fileMeta.Id,
            filename_disk = fileMeta.FilenameDisk,
            filename_download = fileMeta.FilenameDownload,
            type = fileMeta.Type,
            filesize = fileMeta.Filesize,
            width = fileMeta.Width,
            height = fileMeta.Height,
            url,
        });
    }

    // ── Delete File ────────────────────────────────────────────
    // ── Get File Info ──────────────────────────────────────────
    public async Task<object?> GetFileInfoAsync(Guid id, HttpRequest request)
    {
        var fileMeta = await _db.FileMetas.FindAsync(id);
        if (fileMeta == null) return null;

        var baseUrl = $"{request.Scheme}://{request.Host}";
        var url = $"{baseUrl}/uploads/images/{fileMeta.FilenameDisk}";

        return new
        {
            id = fileMeta.Id,
            filename_disk = fileMeta.FilenameDisk,
            filename_download = fileMeta.FilenameDownload,
            type = fileMeta.Type,
            filesize = fileMeta.Filesize,
            width = fileMeta.Width,
            height = fileMeta.Height,
            url,
        };
    }

    // ── Delete File ────────────────────────────────────────────
    public (bool Success, string Message) DeleteFile(string filename)
    {
        var subfolders = new[] { "images", "files" };

        foreach (var folder in subfolders)
        {
            var path = Path.Combine(_env.WebRootPath, "uploads", folder, filename);
            if (!System.IO.File.Exists(path)) continue;

            System.IO.File.Delete(path);
            _logger.LogInformation("File deleted: {Filename}", filename);
            return (true, "File deleted successfully.");
        }

        return (false, "File not found.");
    }

    // ── List Files ─────────────────────────────────────────────
    public async Task<object> GetFilesAsync(int? folderId, HttpRequest request)
    {
        var query = _db.FileMetas.AsQueryable();

        if (folderId.HasValue)
            query = query.Where(f => f.FolderId == folderId);
        else
            query = query.Where(f => f.FolderId == null);

        var files = await query.OrderByDescending(f => f.UploadedOn).ToListAsync();

        var baseUrl = $"{request.Scheme}://{request.Host}";
        return files.Select(f => new
        {
            id = f.Id,
            filename_disk = f.FilenameDisk,
            filename_download = f.FilenameDownload,
            type = f.Type,
            filesize = f.Filesize,
            width = f.Width,
            height = f.Height,
            folder_id = f.FolderId,
            url = $"{baseUrl}/uploads/images/{f.FilenameDisk}",
        }).ToList();
    }

    // ── Move Files ─────────────────────────────────────────────
    public async Task MoveFilesAsync(List<Guid> fileIds, int? folderId)
    {
        var files = await _db.FileMetas
            .Where(f => fileIds.Contains(f.Id))
            .ToListAsync();

        foreach (var file in files)
            file.FolderId = folderId;

        await _db.SaveChangesAsync();
    }

    // ── Delete Files ───────────────────────────────────────────
    public async Task DeleteFilesAsync(List<Guid> fileIds)
    {
        var files = await _db.FileMetas
            .Where(f => fileIds.Contains(f.Id))
            .ToListAsync();

        foreach (var file in files)
        {
            foreach (var folder in new[] { "images", "files" })
            {
                var path = Path.Combine(_env.WebRootPath, "uploads", folder, file.FilenameDisk);
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                    _logger.LogInformation("File deleted: {Filename}", file.FilenameDisk);
                }
            }
        }

        _db.FileMetas.RemoveRange(files);
        await _db.SaveChangesAsync();
    }

    // ── Save File Helper ───────────────────────────────────────
    private async Task<(string FilenameDisk, string StoragePath, string Url)> SaveFileAsync(IFormFile file, Guid fileId, string subfolder, HttpRequest request)
    {
        var uploadsPath = Path.Combine(_env.WebRootPath, "uploads", subfolder);
        Directory.CreateDirectory(uploadsPath);

        var ext = Path.GetExtension(file.FileName).ToLower();
        var filenameDisk = $"{fileId}{ext}";
        var fullPath = Path.Combine(uploadsPath, filenameDisk);

        using var stream = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(stream);

        _logger.LogInformation("File saved: {Filename}", filenameDisk);

        var baseUrl = $"{request.Scheme}://{request.Host}";
        var storagePath = $"/uploads/{subfolder}/{filenameDisk}";
        var url = $"{baseUrl}{storagePath}";

        return (filenameDisk, storagePath, url);
    }
}