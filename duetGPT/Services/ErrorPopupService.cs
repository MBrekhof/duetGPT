namespace duetGPT.Services
{
  public class ErrorPopupService
  {
    public event Action<string> OnError;

    public void ShowError(string message)
    {
      OnError?.Invoke(message);
    }
  }
}
