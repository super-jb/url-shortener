namespace URLShortener.Api.Models;

public class ShortenedUrl
{
    public string ShortCode { get; set; }
    public string OriginalUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}
