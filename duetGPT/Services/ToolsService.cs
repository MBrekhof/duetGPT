using Anthropic.SDK.Common;

namespace duetGPT.Services
{
    public class  ToolsService
    {

        [Function("This function returns current date and time")]
        public static async Task<string> GetCurrentDateTime()
        {
            await Task.Yield(); // Simulate async operation if needed
            return DateTime.UtcNow.ToString("o"); // ISO 8601 format
        }
    }
}
