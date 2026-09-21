using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
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
            return BadRequest("Expiration time must be greater than 0 minutes.");
        }

        var database = _redis.GetDatabase();

        var shortCode = string.IsNullOrWhiteSpace(alias)
            ? Guid.NewGuid().ToString("N")[..6]
            : alias.Trim();
        if (!Regex.IsMatch(shortCode, "^[a-zA-Z0-9]+$"))
        {
            return BadRequest("Short code can only contain letters and numbers.");
        }

        if (shortCode.Length < 3 || shortCode.Length > 20)
        {
            return BadRequest("Short code must be between 3 and 20 characters.");
        }

        var exists = await database.KeyExistsAsync(shortCode);

        if (exists)
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

        return Ok(new ShortUrlResponse
        {
            ShortCode = shortCode,
            ShortUrl = $"http://localhost:5275/api/UrlShortener/{shortCode}"
        });
    }

    [HttpGet("{shortCode}")]
    public async Task<IActionResult> RedirectToLongUrl(string shortCode)
    {
        var database = _redis.GetDatabase();

        var longUrl = await database.StringGetAsync(shortCode);

        if (longUrl.IsNullOrEmpty)
        {
            return NotFound("Short URL not found.");
        }

        // Increase click count
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

        var clicks = await database.StringGetAsync($"clicks:{shortCode}");

        return Ok(new
        {
            ShortCode = shortCode,
            LongUrl = longUrl.ToString(),
            Clicks = clicks.IsNullOrEmpty ? 0 : (long)clicks
        });
    }
}
