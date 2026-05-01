using System.Net.Http;
using System.Text.Json;
using WallpaperManager.Models;

namespace WallpaperManager.Services;

public sealed class SteamWorkshopService
{
    private static readonly HttpClient HttpClient = new();
    private const string ApiUrl = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/";

    public async Task<WorkshopMetadata?> FetchAsync(string workshopId)
    {
        var batch = await FetchBatchAsync([workshopId]);
        return batch.GetValueOrDefault(workshopId);
    }

    public async Task<Dictionary<string, WorkshopMetadata>> FetchBatchAsync(IReadOnlyList<string> workshopIds)
    {
        var result = new Dictionary<string, WorkshopMetadata>(StringComparer.OrdinalIgnoreCase);
        if (workshopIds.Count == 0)
        {
            return result;
        }

        try
        {
            var parameters = new List<KeyValuePair<string, string>>
            {
                new("itemcount", workshopIds.Count.ToString())
            };

            for (var i = 0; i < workshopIds.Count; i++)
            {
                parameters.Add(new($"publishedfileids[{i}]", workshopIds[i]));
            }

            var content = new FormUrlEncodedContent(parameters);
            var response = await HttpClient.PostAsync(ApiUrl, content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("response", out var responseElement) ||
                !responseElement.TryGetProperty("publishedfiledetails", out var details))
            {
                return result;
            }

            foreach (var item in details.EnumerateArray())
            {
                var id = item.TryGetProperty("publishedfileid", out var idProp) ? idProp.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var resultCode = item.TryGetProperty("result", out var resultProp) ? resultProp.GetInt32() : 0;
                if (resultCode != 1)
                {
                    continue;
                }

                var meta = new WorkshopMetadata
                {
                    Title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "",
                    Description = item.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : "",
                    SubscriptionCount = item.TryGetProperty("subscriptions", out var subProp) ? subProp.GetInt64() : 0,
                    FavoriteCount = item.TryGetProperty("favorited", out var favProp) ? favProp.GetInt64() : 0,
                    ViewCount = item.TryGetProperty("views", out var viewProp) ? viewProp.GetInt64() : 0,
                    FileSize = item.TryGetProperty("file_size", out var sizeProp) ? sizeProp.GetInt64() : 0,
                    PreviewUrl = item.TryGetProperty("preview_url", out var prevProp) ? prevProp.GetString() ?? "" : "",
                };

                if (item.TryGetProperty("time_created", out var timeProp))
                {
                    meta.TimeCreated = DateTimeOffset.FromUnixTimeSeconds(timeProp.GetInt64()).DateTime;
                }

                if (item.TryGetProperty("time_updated", out var updProp))
                {
                    meta.TimeUpdated = DateTimeOffset.FromUnixTimeSeconds(updProp.GetInt64()).DateTime;
                }

                // Content descriptor maturity flags
                if (item.TryGetProperty("maybe_inappropriate_sex", out var misProp) && misProp.GetInt32() == 1)
                {
                    meta.IsMature = true;
                    meta.ContentRating = "Questionable";
                }

                if (item.TryGetProperty("maybe_inappropriate_violence", out var mivProp) && mivProp.GetInt32() == 1)
                {
                    meta.IsMature = true;
                }

                if (item.TryGetProperty("banned", out var bannedProp) && bannedProp.GetInt32() == 1)
                {
                    meta.IsAdult = true;
                    meta.ContentRating = "Adult";
                }

                // Parse tags array
                if (item.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tagItem in tagsProp.EnumerateArray())
                    {
                        var tagName = tagItem.TryGetProperty("tag", out var tagVal) ? tagVal.GetString() ?? "" : "";
                        if (!string.IsNullOrWhiteSpace(tagName))
                        {
                            meta.Tags.Add(tagName);
                        }
                    }
                }

                // Also detect maturity via tags
                var lowerTags = meta.Tags.Select(t => t.ToLowerInvariant()).ToList();
                if (lowerTags.Any(t => t is "adult only" or "nsfw" or "adult content" or "mature" or "mature content" or "sexual content" or "erotic"))
                {
                    meta.IsAdult = true;
                }
                if (lowerTags.Any(t => t is "questionable" or "suggestive" or "nudity" or "partial nudity" or "violence"))
                {
                    meta.IsMature = true;
                }

                result[id] = meta;
            }
        }
        catch
        {
            // Silently return what we have so far if the API call fails.
        }

        return result;
    }
}
