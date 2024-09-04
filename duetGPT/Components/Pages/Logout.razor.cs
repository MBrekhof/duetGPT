using Microsoft.AspNetCore.Components;

namespace duetGPT.Components.Pages
{
    public partial class Logout
    {
        async Task LogoutUser()
        {

            if (SignInManager != null)
                await SignInManager.SignOutAsync();

            if (RedirectManager != null)
                RedirectManager.RedirectTo("/");
        }
  
        protected override void OnInitialized()
        {
            Logger.LogInformation("Logout page initialized");
        }        
    }
}