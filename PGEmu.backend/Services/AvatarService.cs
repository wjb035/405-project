using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using PGEmuBackend.Models;

namespace PGEmuBackend.Services;

public class AvatarService
{
    private readonly string _avatarPath;
    // 2 MB file size limit
    private const int MaxFileSizeBytes = 2 * 1024 * 1024; 
    private const int OutputSize = 256;
    
    private static readonly string[] AllowedMimeTypes =
        ["image/jpeg", "image/png", "image/webp", "image/gif"];
    
    public AvatarService(IOptions<Storage> storage)
    {
        _avatarPath = Path.GetFullPath(storage.Value.AvatarPath);
        Directory.CreateDirectory(_avatarPath);
    }
    
    public async Task<string> SaveAvatarAsync(Guid userId, IFormFile file)
    {
        // Checks if the file uploaded is actually viable
        if (file.Length > MaxFileSizeBytes)
            throw new ArgumentException("File exceeds 2MB limit.");

        if (!AllowedMimeTypes.Contains(file.ContentType.ToLower()))
            throw new ArgumentException("Unsupported image format. Please upload a JPEG, PNG, WebP, or GIF.");

        using var image = await Image.LoadAsync(file.OpenReadStream());

        // Resizes file to 256x256
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(OutputSize, OutputSize),
            Mode = ResizeMode.Crop
        }));
        
        // Save as webp
        var fileName = $"{userId}.webp";
        var filePath = Path.Combine(_avatarPath, fileName);

        await image.SaveAsWebpAsync(filePath);

        // URL stored on DB changes with timestamp to refretch new avatar
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"/uploads/avatars/{fileName}?v={timestamp}";
    }

    
    // Does exactly what you think it does
    public void DeleteAvatar(Guid userId)
    {
        var filePath = Path.Combine(_avatarPath, $"{userId}.webp");
        if (File.Exists(filePath))
            File.Delete(filePath);
    }
}