using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using duetGPT.Data;
using duetGPT.Services;
using DevExpress.Blazor;

namespace duetGPT.Components.Pages;

public partial class KnowledgePage
{
  [Inject]
  private ApplicationDbContext Context { get; set; } = default!;

  [Inject]
  private OpenAIService OpenAIService { get; set; } = default!;

  [Inject]
  private AuthenticationStateProvider AuthenticationStateProvider { get; set; } = default!;

  [Inject]
  private IToastNotificationService ToastService { get; set; } = default!;

  private List<Knowledge> GridDataSource { get; set; } = new();
  private bool PopupVisible { get; set; }
  private bool ContentPopupVisible { get; set; }
  private string SelectedContent { get; set; } = "";
  private string SelectedMetadata { get; set; } = "";
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
      ToastService.ShowToast(new ToastOptions()
      {
        ProviderName = "KnowledgeToasts",
        ThemeMode = ToastThemeMode.Dark,
        RenderStyle = ToastRenderStyle.Danger,
        Title = "Error",
        Text = $"Error fetching knowledge data: {ex.Message}"
      });
    }
  }

  private void ShowContentPopup(string content, string? metadata)
  {
    SelectedContent = content;
    SelectedMetadata = metadata ?? "";
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
      OwnerId = knowledge.OwnerId,
      Metadata = knowledge.Metadata
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
          existingItem.Metadata = KnowledgeData.Metadata;
          // Explicitly not updating vectordatastring as per requirements
          Context.Update(existingItem);
        }
      }
      await Context.SaveChangesAsync();
      await LoadData();
      //ToastService.ShowToast(new ToastOptions()
      //{
      //  ProviderName = "KnowledgeToasts",
      //  ThemeMode = ToastThemeMode.Dark,
      //  RenderStyle = ToastRenderStyle.Success,
      //  Title = "Success",
      //  Text = "Knowledge embedded successfully"
      //});
      //ToastService.ShowToast(new ToastOptions()
      //{
      //  ProviderName = "KnowledgeToasts",
      //  ThemeMode = ToastThemeMode.Dark,
      //  RenderStyle = ToastRenderStyle.Success,
      //  Title = "Success",
      //  Text = "Knowledge deleted successfully"
      //});
      //PopupVisible = false;
      ToastService.ShowToast(new ToastOptions()
      {
        ProviderName = "KnowledgeToasts",
        ThemeMode = ToastThemeMode.Dark,
        RenderStyle = ToastRenderStyle.Success,
        Title = "Success",
        Text = KnowledgeData.RagDataId == 0 ? "Knowledge created successfully" : "Knowledge updated successfully"
      });
    }
    catch (Exception ex)
    {
      ToastService.ShowToast(new ToastOptions()
      {
        ProviderName = "KnowledgeToasts",
        ThemeMode = ToastThemeMode.Dark,
        RenderStyle = ToastRenderStyle.Danger,
        Title = "Error",
        Text = $"Error saving knowledge data: {ex.Message}"
      });
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
      ToastService.ShowToast(new ToastOptions()
      {
        ProviderName = "KnowledgeToasts",
        ThemeMode = ToastThemeMode.Dark,
        RenderStyle = ToastRenderStyle.Danger,
        Title = "Error",
        Text = $"Error deleting knowledge: {ex.Message}"
      });
    }
  }

  private async Task EmbedKnowledge(Knowledge knowledge)
  {
    try
    {
      if (knowledge != null && !string.IsNullOrEmpty(knowledge.RagContent))
      {
        // Combine content and metadata for embedding
        var textToEmbed = knowledge.RagContent;
        if (!string.IsNullOrEmpty(knowledge.Metadata))
        {
          textToEmbed += "\n\nMetadata:\n" + knowledge.Metadata;
        }
        var vector = await OpenAIService.GetVectorDataAsync(textToEmbed);
        var existingItem = await Context.Set<Knowledge>().FindAsync(knowledge.RagDataId);
        if (existingItem != null)
        {
          existingItem.VectorDataString = vector;
          Context.Update(existingItem);
          await Context.SaveChangesAsync();
          await LoadData();
        }
      }
            ToastService.ShowToast(new ToastOptions()
            {
                ProviderName = "KnowledgeToasts",
                ThemeMode = ToastThemeMode.Dark,
                RenderStyle = ToastRenderStyle.Success,
                Title = "Success",
                Text = "Knowledge embedded successfully"
            });
        }
        catch (Exception ex)
    {
      ToastService.ShowToast(new ToastOptions()
      {
        ProviderName = "KnowledgeToasts",
        ThemeMode = ToastThemeMode.Dark,
        RenderStyle = ToastRenderStyle.Danger,
        Title = "Error",
        Text = $"Error embedding knowledge: {ex.Message}"
      });
    }
  }
}