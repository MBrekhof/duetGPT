using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using System.IO;
using DevExpress.Blazor;

namespace duetGPT.Components.Pages
{
  public partial class Claude
  {
    [Inject]
    private IToastNotificationService ToastService { get; set; } = default!;

    private string? ImageUrl { get; set; }
    private string? CurrentImagePath { get; set; }  // Store file path instead of byte array
    private string? CurrentImageType { get; set; }
    private bool IsNewThreadPopupVisible { get; set; }
    private const int MaxImageSize = 20 * 1024 * 1024; // 20MB limit
    private const string TempImageFolder = "TempImages";

    private async Task HandleImageUpload(InputFileChangeEventArgs e)
    {
      try
      {
        var file = e.File;
        if (file != null)
        {
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

          // Store file path and type
          CurrentImagePath = filePath;
          CurrentImageType = file.ContentType;

          // Create resized version for UI display
          var resizedImage = await file.RequestImageFileAsync("image/*", 800, 600);
          using (var resizedStream = resizedImage.OpenReadStream(MaxImageSize))
          {
            var buffer = new byte[resizedImage.Size];
            await resizedStream.ReadAsync(buffer);
            ImageUrl = $"data:{file.ContentType};base64,{Convert.ToBase64String(buffer)}";
          }

          // Force UI update
          await InvokeAsync(StateHasChanged);
        }
      }
      catch (Exception ex)
      {
        Logger.LogError(ex, "Error uploading image");
        ToastService.ShowToast(new ToastOptions()
        {
          ProviderName = "ClaudePage",
          ThemeMode = ToastThemeMode.Dark,
          RenderStyle = ToastRenderStyle.Danger,
          Title = "Upload Error",
          Text = $"Failed to upload image: {ex.Message}"
        });
        // Clear image data on error
        await ClearImageData();
      }
    }

    private async Task ClearImageData()
    {
      // Delete temp file if it exists
      if (!string.IsNullOrEmpty(CurrentImagePath) && File.Exists(CurrentImagePath))
      {
        try
        {
          File.Delete(CurrentImagePath);
        }
        catch (Exception ex)
        {
          Logger.LogError(ex, "Error deleting temporary image file");
        }
      }

      ImageUrl = null;
      CurrentImagePath = null;
      CurrentImageType = null;
      await InvokeAsync(StateHasChanged);
    }

    // Method to get image bytes when needed (for sending to LLM)
    public async Task<byte[]?> GetCurrentImageBytes()
    {
      if (string.IsNullOrEmpty(CurrentImagePath) || !File.Exists(CurrentImagePath))
      {
        return null;
      }

      try
      {
        return await File.ReadAllBytesAsync(CurrentImagePath);
      }
      catch (Exception ex)
      {
        Logger.LogError(ex, "Error reading image file");
        return null;
      }
    }

    private void ShowNewThreadConfirmation()
    {
      IsNewThreadPopupVisible = true;
    }

    private async Task ConfirmNewThread()
    {
      IsNewThreadPopupVisible = false;
      await ClearImageData();
      await ClearThread();
    }

    // Cleanup method to be called when component is disposed
    public async Task DisposeAsync()
    {
      await ClearImageData();

      // Cleanup entire temp folder if it exists
      var tempPath = Path.Combine(Directory.GetCurrentDirectory(), TempImageFolder);
      if (Directory.Exists(tempPath))
      {
        try
        {
          Directory.Delete(tempPath, true);
        }
        catch (Exception ex)
        {
          Logger.LogError(ex, "Error cleaning up temp image folder");
        }
      }
    }
  }
}
