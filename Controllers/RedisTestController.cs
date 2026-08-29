using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace UrlShortener.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RedisTestController : ControllerBase
{
    private readonly IConnectionMultiplexer _redis;

    public RedisTestController(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    [HttpGet]
    public async Task<IActionResult> TestRedis()
    {
        var database = _redis.GetDatabase();

        await database.StringSetAsync("message", "Hello Redis!");

        var value = await database.StringGetAsync("message");

        return Ok(value.ToString());
    }
}