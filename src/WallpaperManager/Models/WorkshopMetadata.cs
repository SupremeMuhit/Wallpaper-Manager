namespace WallpaperManager.Models;

public sealed class WorkshopMetadata
{
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string> Tags { get; set; } = [];

    public long SubscriptionCount { get; set; }

    public long FavoriteCount { get; set; }

    public long ViewCount { get; set; }

    public long FileSize { get; set; }

    public DateTime TimeCreated { get; set; }

    public DateTime TimeUpdated { get; set; }

    public string PreviewUrl { get; set; } = string.Empty;

    /// <summary>Steam "Adult" / banned content → NSFW in our app.</summary>
    public bool IsAdult { get; set; }

    /// <summary>Steam "Questionable" / suggestive content → Mature in our app.</summary>
    public bool IsMature { get; set; }

    public string ContentRating { get; set; } = string.Empty;
}
