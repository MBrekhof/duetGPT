using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using duetGPT.Data;
using duetGPT.Services;

namespace duetGPT.Components.Pages;

public partial class KnowledgePage
{
  [Inject]
  private ApplicationDbContext Context { get; set; } = default!;

  [Inject]
  private OpenAIService OpenAIService { get; set; } = default!;

  [Inject]
  private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

  private List<Knowledge> GridDataSource { get; set; } = new();
  private bool PopupVisible { get; set; }
  private bool ContentPopupVisible { get; set; }
  private string SelectedContent { get; set; } = "";
  private string PopupTitle => KnowledgeData?.RagDataId == 0 ? "New Knowledge" : "Edit Knowledge";
  private Knowledge KnowledgeData { get; set; } = new();
  private string CurrentUserId { get; set; } = "";

  protected override async Task OnInitializedAsync()
  {
    var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
    CurrentUserId = authState.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
    await LoadData();
  }

  private async Task LoadData()
  {
    try
    {
      GridDataSource = await Context.Set<Knowledge>()
          .Include(k => k.Owner)
          .Where(d => d.OwnerId == CurrentUserId)
          .ToListAsync();
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error fetching knowledge data: {ex.Message}");
    }
  }

  private void ShowContentPopup(string content)
  {
    SelectedContent = content;
    ContentPopupVisible = true;
  }

  private void ShowEditPopup(Knowledge? knowledge)
  {
    KnowledgeData = knowledge != null ? new Knowledge
    {
      RagDataId = knowledge.RagDataId,
      Title = knowledge.Title,
      RagContent = knowledge.RagContent,
      Tokens = knowledge.Tokens,
      CreationDate = knowledge.CreationDate,
      VectorDataString = knowledge.VectorDataString,
      OwnerId = knowledge.OwnerId
    } : new Knowledge
    {
      CreationDate = DateTime.UtcNow,
      OwnerId = CurrentUserId
    };
    PopupVisible = true;
  }

  private async Task SaveKnowledgeData()
  {
    try
    {
      if (KnowledgeData.RagDataId == 0)
      {
        KnowledgeData.OwnerId = CurrentUserId;
        Context.Add(KnowledgeData);
      }
      else
      {
        var existingItem = await Context.Set<Knowledge>().FindAsync(KnowledgeData.RagDataId);
        if (existingItem != null)
        {
          existingItem.Title = KnowledgeData.Title;
          existingItem.RagContent = KnowledgeData.RagContent;
          existingItem.Tokens = KnowledgeData.Tokens;
          // Explicitly not updating vectordatastring as per requirements
          Context.Update(existingItem);
        }
      }
      await Context.SaveChangesAsync();
      await LoadData();
      PopupVisible = false;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error saving knowledge data: {ex.Message}");
    }
  }

  private async Task DeleteKnowledge(int knowledgeId)
  {
    try
    {
      var knowledge = await Context.Set<Knowledge>().FindAsync(knowledgeId);
      if (knowledge != null)
      {
        Context.Remove(knowledge);
        await Context.SaveChangesAsync();
        await LoadData();
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error deleting knowledge: {ex.Message}");
    }
  }

  private async Task EmbedKnowledge(Knowledge knowledge)
  {
    try
    {
      if (knowledge != null && !string.IsNullOrEmpty(knowledge.RagContent))
      {
        var vector = await OpenAIService.GetVectorDataAsync(knowledge.RagContent);
        var existingItem = await Context.Set<Knowledge>().FindAsync(knowledge.RagDataId);
        if (existingItem != null)
        {
          existingItem.VectorDataString = vector;
          Context.Update(existingItem);
          await Context.SaveChangesAsync();
          await LoadData();
        }
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error embedding knowledge: {ex.Message}");
    }
  }
}