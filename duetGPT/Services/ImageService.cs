using Microsoft.AspNetCore.Components.Forms;

namespace duetGPT.Services
{
  public interface IImageService
  {
    Task<ImageUploadResult> HandleImageUploadAsync(IBrowserFile file);
    Task<byte[]?> GetImageBytesAsync(string imagePath);
    Task ClearImageAsync(string? imagePath);
    Task CleanupTempFolderAsync();
  }

  public record ImageUploadResult
  {
    public required string TempFilePath { get; init; }
    public required string ImageType { get; init; }
    public required string DisplayDataUrl { get; init; }
  }

  public class ImageService : IImageService
  {
    private readonly ILogger<ImageService> _logger;
    private const int MaxImageSize = 20 * 1024 * 1024; // 20MB limit
    private const string TempImageFolder = "TempImages";

    public ImageService(ILogger<ImageService> logger)
    {
      _logger = logger;
    }

    public async Task<ImageUploadResult> HandleImageUploadAsync(IBrowserFile file)
    {
      try
      {
        if (file == null)
        {
          throw new ArgumentNullException(nameof(file), "File cannot be null");
        }

        // Check file size
        if (file.Size > MaxImageSize)
        {
          throw new Exception($"Image size exceeds maximum limit of {MaxImageSize / (1024 * 1024)}MB");
        }

        // Ensure temp folder exists
        var tempPath = Path.Combine(Directory.GetCurrentDirectory(), TempImageFolder);
        Directory.CreateDirectory(tempPath);

        // Generate unique filename
        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.Name)}";
        var filePath = Path.Combine(tempPath, fileName);

        // Save original file to disk
        using (var originalStream = file.OpenReadStream(MaxImageSize))
        using (var fileStream = new FileStream(filePath, FileMode.Create))
        {
          await originalStream.CopyToAsync(fileStream);
        }

        // Create resized version for UI display with max dimension of 800px while maintaining aspect ratio
        var resizedImage = await file.RequestImageFileAsync(file.ContentType, 800, 800);
        using (var resizedStream = resizedImage.OpenReadStream(MaxImageSize))
        {
          var buffer = new byte[resizedImage.Size];
          await resizedStream.ReadAsync(buffer);
          var displayDataUrl = $"data:{file.ContentType};base64,{Convert.ToBase64String(buffer)}";

          _logger.LogInformation("Image uploaded successfully: {FileName}", fileName);

          return new ImageUploadResult
          {
            TempFilePath = filePath,
            ImageType = file.ContentType,
            DisplayDataUrl = displayDataUrl
          };
        }
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error uploading image");
        throw;
      }
    }

    public async Task<byte[]?> GetImageBytesAsync(string imagePath)
    {
      if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
      {
        _logger.LogWarning("Image file not found: {ImagePath}", imagePath);
        return null;
      }

      try
      {
        var bytes = await File.ReadAllBytesAsync(imagePath);
        _logger.LogInformation("Retrieved image bytes from: {ImagePath}", imagePath);
        return bytes;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error reading image file: {ImagePath}", imagePath);
        return null;
      }
    }

    public async Task ClearImageAsync(string? imagePath)
    {
      // Delete temp file if it exists
      if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
      {
        try
        {
          File.Delete(imagePath);
          _logger.LogInformation("Deleted temporary image file: {ImagePath}", imagePath);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Error deleting temporary image file: {ImagePath}", imagePath);
        }
      }

      await Task.CompletedTask;
    }

    public async Task CleanupTempFolderAsync()
    {
      // Cleanup entire temp folder if it exists
      var tempPath = Path.Combine(Directory.GetCurrentDirectory(), TempImageFolder);
      if (Directory.Exists(tempPath))
      {
        try
        {
          Directory.Delete(tempPath, true);
          _logger.LogInformation("Cleaned up temp image folder: {TempPath}", tempPath);
        }
        catch (Exception ex)
        {
          _logger.LogError(ex, "Error cleaning up temp image folder: {TempPath}", tempPath);
        }
      }

      await Task.CompletedTask;
    }
  }
}
