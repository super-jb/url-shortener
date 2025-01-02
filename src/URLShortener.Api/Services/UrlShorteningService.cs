using Dapper;
using Microsoft.Extensions.Caching.Hybrid;
using Npgsql;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using URLShortener.Api.Models;

namespace URLShortener.Api.Services;

public sealed class UrlShorteningService(
    NpgsqlDataSource dataSource,
    HybridCache hybridCache,
    IHttpContextAccessor httpContextAccessor,
    ILogger<UrlShorteningService> logger)
{
    private const int MaxRetries = 3;

    private static readonly Meter Meter = new ("UrlShortener.Api");
    private static readonly Counter<int> RedirectsCounter = Meter.CreateCounter<int>(
        "url_shortener.redirects",
        "Number of successful redirects");
    private static readonly Counter<int> FailedRedirectsCounter = Meter.CreateCounter<int>(
        "url_shortener.failed_redirects",
        "Number of failed redirects");

    public async Task<string> ShortenUrlAsync(string originalUrl, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var shortCode = GenerateShortCode();

                const string sql =
                    """
                    INSERT INTO shortened_urls (short_code, original_url)
                    VALUES (@ShortCode, @OriginalUrl)
                    RETURNING short_code
                    """;

                await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);

                var result = await conn.QuerySingleAsync<string>(sql, new { ShortCode = shortCode, OriginalUrl = originalUrl });

                await hybridCache.SetAsync(shortCode, originalUrl, cancellationToken: cancellationToken);

                return result;
            }
            catch (PostgresException ex)
                when (ex.SqlState == PostgresErrorCodes.UniqueViolation) // "23505"
            {
                if (attempt == MaxRetries)
                {
                    logger.LogError(ex, "Failed to generate a unique short code after {MaxRetries} attempts", MaxRetries);
                    throw new InvalidOperationException("Failed to generate unique short code", ex);
                }

                logger.LogWarning("ShortCode collision. Retrying... (Attempt: {Attempt} of {MaxRetries})", attempt + 1, MaxRetries);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to generate a unique short code");
            }
        }

        throw new InvalidOperationException("Failed to generate unique short code");
    }

    public async Task<string?> GetOriginalUrlAsync(string shortCode, CancellationToken cancellationToken)
    {
        var originalUrl =
            await hybridCache.GetOrCreateAsync(shortCode, async cancellationToken =>
            {
                const string sql =
                """
                SELECT original_url as OriginalUrl 
                FROM shortened_urls
                WHERE short_code = @ShortCode
                """;

                await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);

                return await conn.QueryFirstOrDefaultAsync<string>(sql, new { ShortCode = shortCode });
            });

        var tags = new TagList { { "short_code", shortCode } };
        if (string.IsNullOrEmpty(originalUrl))
        {
            FailedRedirectsCounter.Add(1, tags);
        }
        else
        {
            await RecordVisitAsync(shortCode, cancellationToken);
            RedirectsCounter.Add(1, tags);
        }

        return originalUrl;
    }

    public async Task<IEnumerable<ShortenedUrl>> GetAllUrlsAsync(CancellationToken cancellationToken)
    {
        const string sql =
            """
            SELECT 
              short_code as ShortCode,
              original_url as OriginalUrl,
              created_at as CreatedAt
            FROM shortened_urls
            ORDER BY created_at DESC
            """;

        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);
        return await conn.QueryAsync<ShortenedUrl>(sql, cancellationToken);
    }

    private static string GenerateShortCode()
    {
        const int length = 7;
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        var chars = Enumerable.Range(0, length)
            .Select(_ => alphabet[Random.Shared.Next(alphabet.Length)])
            .ToArray();
        return new string(chars);
    }

    private async Task RecordVisitAsync(string shortCode, CancellationToken cancellationToken)
    {
        var context = httpContextAccessor.HttpContext;
        var userAgent = context?.Request.Headers.UserAgent.ToString();
        var referer = context?.Request.Headers.Referer.ToString();

        const string sql =
            """
            INSERT INTO url_visits (short_code, user_agent, referer)
            VALUES (@ShortCode, @UserAgent, @Referer);
            """;

        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);

        await conn.ExecuteAsync(sql, new { ShortCode = shortCode, UserAgent = userAgent, Referer = referer });
    }
}
