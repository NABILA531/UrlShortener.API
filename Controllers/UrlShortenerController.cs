
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System.Security.Claims;
using System.Text.RegularExpressions;
using UrlShortener.API.Models;

namespace UrlShortener.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UrlShortenerController : ControllerBase
{
    private readonly IConnectionMultiplexer _redis;

    public UrlShortenerController(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> ShortenUrl(
        [FromBody] string longUrl,
        [FromQuery] string? alias = null,
        [FromQuery] int? expirationMinutes = null)
    {
        if (string.IsNullOrWhiteSpace(longUrl))
        {
            return BadRequest("Long URL cannot be empty.");
        }

        if (!Uri.TryCreate(longUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            return BadRequest("Please provide a valid HTTP or HTTPS URL.");
        }

        if (expirationMinutes.HasValue && expirationMinutes.Value <= 0)
        {
            return BadRequest(
                "Expiration time must be greater than 0 minutes.");
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User ID not found in token.");
        }

        var database = _redis.GetDatabase();

        var shortCode = string.IsNullOrWhiteSpace(alias)
            ? Guid.NewGuid().ToString("N")[..6]
            : alias.Trim();

        if (!Regex.IsMatch(shortCode, "^[a-zA-Z0-9]+$"))
        {
            return BadRequest(
                "Short code can only contain letters and numbers.");
        }

        if (shortCode.Length < 3 || shortCode.Length > 20)
        {
            return BadRequest(
                "Short code must be between 3 and 20 characters.");
        }

        if (await database.KeyExistsAsync(shortCode))
        {
            return Conflict("This short code is already in use.");
        }

        if (expirationMinutes.HasValue)
        {
            await database.StringSetAsync(
                shortCode,
                longUrl,
                TimeSpan.FromMinutes(expirationMinutes.Value));
        }
        else
        {
            await database.StringSetAsync(shortCode, longUrl);
        }

        await database.SetAddAsync(
            $"user:urls:{userId}",
            shortCode);

        return Ok(new ShortUrlResponse
        {
            ShortCode = shortCode,
            ShortUrl =
            $"http://localhost:5000/api/UrlShortener/{shortCode}"
        });
    }

    [HttpGet("my")]
    [Authorize]
    public async Task<IActionResult> GetMyUrls()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User ID not found in token.");
        }

        var database = _redis.GetDatabase();

        var shortCodes = await database.SetMembersAsync(
            $"user:urls:{userId}");

        var urls = new List<object>();

        foreach (var code in shortCodes)
        {
            var shortCode = code.ToString();

            var longUrl = await database.StringGetAsync(shortCode);

            if (longUrl.IsNullOrEmpty)
            {
                continue;
            }

            var clicks = await database.StringGetAsync(
                $"clicks:{shortCode}");

            urls.Add(new
            {
                ShortCode = shortCode,
                ShortUrl =
    $"http://localhost:5000/api/UrlShortener/{shortCode}",
                LongUrl = longUrl.ToString(),
                Clicks = clicks.IsNullOrEmpty ? 0 : (long)clicks
            });
        }

        return Ok(urls);
    }

    [HttpGet("{shortCode}")]
    public async Task<IActionResult> RedirectToLongUrl(
        string shortCode)
    {
        var database = _redis.GetDatabase();

        var longUrl = await database.StringGetAsync(shortCode);

        if (longUrl.IsNullOrEmpty)
        {
            return NotFound("Short URL not found.");
        }

        await database.StringIncrementAsync($"clicks:{shortCode}");

        return Redirect(longUrl.ToString());
    }

    [HttpGet("{shortCode}/stats")]
    public async Task<IActionResult> GetStats(string shortCode)
    {
        var database = _redis.GetDatabase();

        var longUrl = await database.StringGetAsync(shortCode);

        if (longUrl.IsNullOrEmpty)
        {
            return NotFound("Short URL not found.");
        }

        var clicks = await database.StringGetAsync(
            $"clicks:{shortCode}");

        return Ok(new
        {
            ShortCode = shortCode,
            LongUrl = longUrl.ToString(),
            Clicks = clicks.IsNullOrEmpty ? 0 : (long)clicks
        });
    }

    [HttpDelete("{shortCode}")]
    [Authorize]
    public async Task<IActionResult> DeleteUrl(string shortCode)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("User ID not found in token.");
        }


        var database = _redis.GetDatabase();
        var userUrlsKey = $"user:urls:{userId}";

        var isOwner = await database.SetContainsAsync(
            userUrlsKey, shortCode);

        if (!isOwner)
        {
            return NotFound("Short URL not found in your account.");
        }

        await database.KeyDeleteAsync(shortCode);
        await database.KeyDeleteAsync($"clicks:{shortCode}");
        await database.SetRemoveAsync(userUrlsKey, shortCode);

        return Ok(new
        {
            Message = "Short URL deleted successfully.",
            ShortCode = shortCode
        });
    }
}
