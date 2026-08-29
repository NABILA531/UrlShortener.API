using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

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
    public async Task<IActionResult> ShortenUrl([FromBody] string longUrl)
    {
        var database = _redis.GetDatabase();

        var shortCode = Guid.NewGuid().ToString("N")[..6];

        await database.StringSetAsync(shortCode, longUrl);

        return Ok(new
        {
            ShortCode = shortCode,
            ShortUrl = $"http://localhost:5275/api/UrlShortener/{shortCode}"
        });
    }
}