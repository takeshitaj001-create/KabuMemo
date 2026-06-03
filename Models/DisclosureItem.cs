using System.Text.Json.Serialization;

namespace KabuMemo.Models;

public class DisclosureItem
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("stockName")]
    public string StockName { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("time")]
    public string Time { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

public class DisclosureCache
{
    [JsonPropertyName("fetchedAt")]
    public DateTime FetchedAt { get; set; }

    [JsonPropertyName("items")]
    public List<DisclosureItem> Items { get; set; } = [];
}
