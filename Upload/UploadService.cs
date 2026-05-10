namespace DotnetStarterKit.Upload;

public class UploadService
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<UploadService> _logger;

    private static readonly string[] AllowedImageTypes = ["image/jpeg", "image/png", "image/webp", "image/gif"];
    private const long MaxImageSize = 5 * 1024 * 1024; // 5MB
    private const long MaxFileSize = 10 * 1024 * 1024; // 10MB

    public UploadService(IWebHostEnvironment env, ILogger<UploadService> logger)
    {
        _env = env;
        _logger = logger;
    }

    // ── Upload Image ───────────────────────────────────────────
    public async Task<(bool Success, string Message, string? Url)> UploadImageAsync(IFormFile file, HttpRequest request)
    {
        if (file.Length == 0)
            return (false, "No file provided.", null);

        if (file.Length > MaxImageSize)
            return (false, "Image must be less than 5MB.", null);

        if (!AllowedImageTypes.Contains(file.ContentType.ToLower()))
            return (false, "Only JPEG, PNG, WEBP, and GIF images are allowed.", null);

        var url = await SaveFileAsync(file, "images", request);
        return (true, "Image uploaded successfully.", url);
    }

    // ── Upload File ────────────────────────────────────────────
    public async Task<(bool Success, string Message, string? Url)> UploadFileAsync(IFormFile file, HttpRequest request)
    {
        if (file.Length == 0)
            return (false, "No file provided.", null);

        if (file.Length > MaxFileSize)
            return (false, "File must be less than 10MB.", null);

        var url = await SaveFileAsync(file, "files", request);
        return (true, "File uploaded successfully.", url);
    }

    // ── Delete File ────────────────────────────────────────────
    public (bool Success, string Message) DeleteFile(string filename)
    {
        // Search in both subfolders
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

    // ── Save File Helper ───────────────────────────────────────
    private async Task<string> SaveFileAsync(IFormFile file, string subfolder, HttpRequest request)
    {
        var uploadsPath = Path.Combine(_env.WebRootPath, "uploads", subfolder);
        Directory.CreateDirectory(uploadsPath);

        var ext = Path.GetExtension(file.FileName).ToLower();
        var filename = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(uploadsPath, filename);

        using var stream = new FileStream(fullPath, FileMode.Create);
        await file.CopyToAsync(stream);

        _logger.LogInformation("File saved: {Filename}", filename);

        var baseUrl = $"{request.Scheme}://{request.Host}";
        return $"{baseUrl}/uploads/{subfolder}/{filename}";
    }
}