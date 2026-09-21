namespace UrlShortener.API.Models;

public class ShortUrlResponse
{
    public string ShortCode { get; set; } = string.Empty;
    public string ShortUrl { get; set; } = string.Empty;
}