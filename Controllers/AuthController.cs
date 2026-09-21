
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UrlShortener.API.Models;

namespace UrlShortener.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IConnectionMultiplexer _redis;

    private const string JwtKey = "UrlShortenerSuperSecretKey123456789";

    public AuthController(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] User user)
    {
        if (string.IsNullOrWhiteSpace(user.Username) ||
            string.IsNullOrWhiteSpace(user.Email) ||
            string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return BadRequest("Username, email and password are required.");
        }

        var database = _redis.GetDatabase();

        var emailKey = $"user:email:{user.Email.ToLower()}";

        var existingUser = await database.StringGetAsync(emailKey);

        if (!existingUser.IsNullOrEmpty)
        {
            return Conflict("A user with this email already exists.");
        }

        user.Id = Guid.NewGuid().ToString();

        user.PasswordHash = HashPassword(user.PasswordHash);

        var userJson = JsonSerializer.Serialize(user);

        await database.StringSetAsync(
            $"user:{user.Id}",
            userJson);

        await database.StringSetAsync(
            emailKey,
            user.Id);

        return Ok(new
        {
            Message = "User registered successfully.",
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] User loginUser)
    {
        if (string.IsNullOrWhiteSpace(loginUser.Email) ||
            string.IsNullOrWhiteSpace(loginUser.PasswordHash))
        {
            return BadRequest("Email and password are required.");
        }

        var database = _redis.GetDatabase();

        var emailKey = $"user:email:{loginUser.Email.ToLower()}";

        var userId = await database.StringGetAsync(emailKey);

        if (userId.IsNullOrEmpty)
        {
            return Unauthorized("Invalid email or password.");
        }

        var userJson = await database.StringGetAsync($"user:{userId}");

        if (userJson.IsNullOrEmpty)
        {
            return Unauthorized("Invalid email or password.");
        }

        var user = JsonSerializer.Deserialize<User>(userJson.ToString());

        if (user == null)
        {
            return Unauthorized("Invalid email or password.");
        }

        var hashedPassword = HashPassword(loginUser.PasswordHash);

        if (user.PasswordHash != hashedPassword)
        {
            return Unauthorized("Invalid email or password.");
        }

        var token = GenerateJwtToken(user);

        return Ok(new
        {
            Message = "Login successful.",
            UserId = user.Id,
            Username = user.Username,
            Email = user.Email,
            Token = token
        });
    }

    private static string GenerateJwtToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email)
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(JwtKey));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();

        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);

        return Convert.ToBase64String(hash);
    }
}