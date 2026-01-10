using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using duetGPT.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace duetGPT.Controllers
{
  [ApiController]
  [Route("api/[controller]")]
  public class AuthController : ControllerBase
  {
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
      _userManager = userManager;
      _signInManager = signInManager;
      _configuration = configuration;
      _logger = logger;
    }

    // POST: api/auth/login
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto request)
    {
      try
      {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
          return Unauthorized(new { message = "Invalid email or password" });

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
        if (!result.Succeeded)
          return Unauthorized(new { message = "Invalid email or password" });

        var token = GenerateJwtToken(user);

        return Ok(new LoginResponseDto
        {
          Token = token,
          Email = user.Email ?? string.Empty,
          UserId = user.Id
        });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error during login");
        return StatusCode(500, new { message = "An error occurred during login" });
      }
    }

    // POST: api/auth/register
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<RegisterResponseDto>> Register([FromBody] RegisterRequestDto request)
    {
      try
      {
        if (request.Password != request.ConfirmPassword)
          return BadRequest(new { message = "Passwords do not match" });

        var user = new ApplicationUser
        {
          UserName = request.Email,
          Email = request.Email
        };

        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
          var errors = string.Join(", ", result.Errors.Select(e => e.Description));
          return BadRequest(new { message = errors });
        }

        return Ok(new RegisterResponseDto
        {
          Message = "Registration successful. Please log in.",
          UserId = user.Id
        });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error during registration");
        return StatusCode(500, new { message = "An error occurred during registration" });
      }
    }

    // POST: api/auth/logout
    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult> Logout()
    {
      try
      {
        await _signInManager.SignOutAsync();
        return Ok(new { message = "Logout successful" });
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Error during logout");
        return StatusCode(500, new { message = "An error occurred during logout" });
      }
    }

    private string GenerateJwtToken(ApplicationUser user)
    {
      var jwtSettings = _configuration.GetSection("JwtSettings");
      var secretKey = jwtSettings["SecretKey"] ?? "YourSuperSecretKeyThatIsAtLeast32CharactersLong!";
      var issuer = jwtSettings["Issuer"] ?? "duetGPT";
      var audience = jwtSettings["Audience"] ?? "duetGPT-users";

      var claims = new[]
      {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

      var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
      var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

      var token = new JwtSecurityToken(
          issuer: issuer,
          audience: audience,
          claims: claims,
          expires: DateTime.UtcNow.AddDays(7),
          signingCredentials: credentials
      );

      return new JwtSecurityTokenHandler().WriteToken(token);
    }
  }

  // DTOs
  public class LoginRequestDto
  {
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
  }

  public class LoginResponseDto
  {
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
  }

  public class RegisterRequestDto
  {
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
  }

  public class RegisterResponseDto
  {
    public string Message { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
  }
}
