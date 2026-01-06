namespace duetGPT.Services
{
    /// <summary>
    /// Service to share chat context between UI components and the IChatClient adapter
    /// </summary>
    public interface IChatContextService
    {
        IEnumerable<int> SelectedFiles { get; set; }
        int ThreadId { get; set; }
        string? CustomPrompt { get; set; }
        bool EnableRag { get; set; }
        bool EnableWebSearch { get; set; }
        bool EnableExtendedThinking { get; set; }
        string ModelId { get; set; }
    }

    public class ChatContextService : IChatContextService
    {
        public IEnumerable<int> SelectedFiles { get; set; } = Enumerable.Empty<int>();
        public int ThreadId { get; set; }
        public string? CustomPrompt { get; set; }
        public bool EnableRag { get; set; } = true;
        public bool EnableWebSearch { get; set; }
        public bool EnableExtendedThinking { get; set; }
        public string ModelId { get; set; } = "claude-sonnet-4-5-20250929";
    }
}
