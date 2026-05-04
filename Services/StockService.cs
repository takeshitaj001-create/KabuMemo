using System.Text.Json;
using KabuMemo.Models;
using Microsoft.JSInterop;

namespace KabuMemo.Services;

public class StockService(IJSRuntime js)
{
    private const string StorageKey = "kabumemo_stocks";

    public async Task<List<StockItem>> GetAllAsync()
    {
        var json = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<StockItem>>(json) ?? []; }
        catch { return []; }
    }

    public async Task SaveAsync(List<StockItem> items)
    {
        var json = JsonSerializer.Serialize(items);
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
    }
}
