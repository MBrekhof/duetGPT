using Microsoft.AspNetCore.Components.Forms;

namespace duetGPT.Components.Pages
{
  public partial class Claude
  {
    private string? ImageUrl { get; set; }
    private bool IsNewThreadPopupVisible { get; set; }

    private async Task HandleImageUpload(InputFileChangeEventArgs e)
    {
      try
      {
        var file = e.File;
        if (file != null)
        {
          var resizedImage = await file.RequestImageFileAsync("image/*", 800, 600);
          var buffer = new byte[resizedImage.Size];
          await resizedImage.OpenReadStream().ReadAsync(buffer);
          ImageUrl = $"data:{file.ContentType};base64,{Convert.ToBase64String(buffer)}";
          StateHasChanged();
        }
      }
      catch (Exception ex)
      {
        Logger.LogError(ex, "Error uploading image");
        ErrorService.ShowError($"Failed to upload image: {ex.Message}");
      }
    }

    private void ShowNewThreadConfirmation()
    {
      IsNewThreadPopupVisible = true;
    }

    private async Task ConfirmNewThread()
    {
      IsNewThreadPopupVisible = false;
      ImageUrl = null;
      await ClearThread();
    }
  }
}
