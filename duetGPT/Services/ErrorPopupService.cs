using Microsoft.Extensions.Logging;

namespace duetGPT.Services
{
  public class ErrorPopupService
  {
    private readonly ILogger<ErrorPopupService> _logger;
    public event Action<string> OnError;

    public ErrorPopupService(ILogger<ErrorPopupService> logger)
    {
      _logger = logger;
    }

    public void ShowError(string message)
    {
      try
      {
        _logger.LogError("Error popup displayed: {Message}", message);
        OnError?.Invoke(message);
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to show error popup with message: {Message}", message);
        throw;
      }
    }
  }
}
