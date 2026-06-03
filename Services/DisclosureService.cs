using System.Net.Http.Json;
using System.Text.Json;
using KabuMemo.Models;
using Microsoft.JSInterop;

namespace KabuMemo.Services;

public partial class DisclosureService(HttpClient http, IJSRuntime js)
{
    private const string CacheKey = "kabumemo_disclosures";
    private const string LastSeenKey = "kabumemo_disclosures_seen";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private const string CfWorkerBase = "https://kabumemo-proxy.kabumemo.workers.dev";

    // ---- キャッシュ操作 ----

    public async Task<DisclosureCache?> GetCacheAsync()
    {
        var json = await js.InvokeAsync<string?>("localStorage.getItem", CacheKey);
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<DisclosureCache>(json); }
        catch { return null; }
    }

    public bool IsCacheValid(DisclosureCache? cache) =>
        cache != null && (DateTime.Now - cache.FetchedAt) < CacheTtl;

    public async Task SaveCacheAsync(List<DisclosureItem> items)
    {
        var cache = new DisclosureCache { FetchedAt = DateTime.Now, Items = items };
        await js.InvokeVoidAsync("localStorage.setItem", CacheKey, JsonSerializer.Serialize(cache));
    }

    public async Task<string> GetLastSeenAsync() =>
        await js.InvokeAsync<string?>("localStorage.getItem", LastSeenKey) ?? "";

    public async Task MarkAllSeenAsync() =>
        await js.InvokeVoidAsync("localStorage.setItem", LastSeenKey,
            DateTime.Now.ToString("yyyy-MM-dd HH:mm"));

    // ---- 取得処理 ----

    public async Task<List<DisclosureItem>> GetRecentAsync(StockItem stock, int count = 3)
    {
        var cache = await GetCacheAsync();
        if (IsCacheValid(cache) && cache!.Items.Any(x => x.Code == stock.Code))
        {
            return cache.Items
                .Where(x => x.Code == stock.Code)
                .OrderByDescending(x => x.Date + x.Time)
                .Take(count)
                .ToList();
        }

        var (items, _) = await FetchForStockAsync(stock);
        return items
            .OrderByDescending(x => x.Date + x.Time)
            .Take(count)
            .ToList();
    }

    public async Task<(List<DisclosureItem> items, List<string> errors)> FetchAllAsync(
        IReadOnlyList<StockItem> stocks,
        Action<int, int>? onProgress = null)
    {
        var allItems = new List<DisclosureItem>();
        var errors = new List<string>();

        for (int i = 0; i < stocks.Count; i++)
        {
            onProgress?.Invoke(i, stocks.Count);
            var (items, err) = await FetchForStockAsync(stocks[i]);
            if (err != null) errors.Add($"[{stocks[i].Code}] {err}");
            allItems.AddRange(items);
            if (i < stocks.Count - 1)
                await Task.Delay(400); // レート制限
        }

        onProgress?.Invoke(stocks.Count, stocks.Count);

        // 日付降順で統合
        return (allItems.OrderByDescending(x => x.Date + x.Time).ToList(), errors);
    }

    private async Task<(List<DisclosureItem>, string?)> FetchForStockAsync(StockItem stock)
    {
        try
        {
            var url = $"{CfWorkerBase}/api/disclosures/{stock.Code}";
            var rawItems = await http.GetFromJsonAsync<List<CfDisclosureItem>>(url);
            if (rawItems == null) return ([], "レスポンスが空です");

            var items = rawItems.Select(r => new DisclosureItem
            {
                Code = stock.Code,
                StockName = stock.Name,
                Date = r.Date,
                Time = r.Time,
                Title = r.Title,
                Category = InferCategory(r.Title),
                Url = r.Url,
            }).ToList();

            return (items, null);
        }
        catch (HttpRequestException ex)
        {
            var code = ex.StatusCode.HasValue ? $"HTTP {(int)ex.StatusCode}" : "HTTP エラー";
            return ([], $"{code}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return ([], "タイムアウト（15秒）");
        }
        catch (Exception ex)
        {
            return ([], $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string InferCategory(string title)
    {
        if (title.Contains("決算")) return "決算";
        if (title.Contains("配当")) return "配当";
        if (title.Contains("業績") || title.Contains("修正")) return "業績";
        if (title.Contains("株主総会")) return "株主総会";
        if (title.Contains("自己株")) return "自己株式";
        if (title.Contains("合併") || title.Contains("買収") || title.Contains("TOB")) return "M&A";
        if (title.Contains("役員") || title.Contains("代表取締役")) return "役員";
        if (title.Contains("増資") || title.Contains("株式分割")) return "資本政策";
        return "開示";
    }
}

// CF Worker から返ってくる JSON の DTO
public class CfDisclosureItem
{
    [System.Text.Json.Serialization.JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("time")]
    public string Time { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [System.Text.Json.Serialization.JsonPropertyName("url")]
    public string Url { get; set; } = "";
}
