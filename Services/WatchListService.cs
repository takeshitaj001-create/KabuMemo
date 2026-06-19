using System.Text.Json;
using KabuMemo.Models;
using Microsoft.JSInterop;

namespace KabuMemo.Services;

public class WatchListService(IJSRuntime js, StockService stockService)
{
    private const string ListsKey = "kabumemo_watchlists";
    private const string ActiveKey = "kabumemo_active_list";
    private const string DefaultListName = "マイリスト";

    public async Task<List<WatchList>> GetAllAsync()
    {
        var json = await js.InvokeAsync<string?>("localStorage.getItem", ListsKey);
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<WatchList>>(json) ?? []; }
        catch { return []; }
    }

    public async Task SaveAllAsync(List<WatchList> lists)
    {
        var json = JsonSerializer.Serialize(lists);
        await js.InvokeVoidAsync("localStorage.setItem", ListsKey, json);
    }

    public async Task<string?> GetActiveIdAsync()
        => await js.InvokeAsync<string?>("localStorage.getItem", ActiveKey);

    public async Task SetActiveIdAsync(string id)
        => await js.InvokeVoidAsync("localStorage.setItem", ActiveKey, id);

    // 初回起動時のマイグレーション：kabumemo_watchlists が存在しない場合に「マイリスト」を自動生成
    public async Task<List<WatchList>> InitializeAsync()
    {
        var json = await js.InvokeAsync<string?>("localStorage.getItem", ListsKey);
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var existing = JsonSerializer.Deserialize<List<WatchList>>(json);
                if (existing != null) return existing;
            }
            catch { }
        }

        var stocks = await stockService.GetAllAsync();
        var defaultList = new WatchList
        {
            Name = DefaultListName,
            StockCodes = stocks.Select(s => s.Code).ToList()
        };
        var lists = new List<WatchList> { defaultList };
        await SaveAllAsync(lists);
        await SetActiveIdAsync(defaultList.Id);
        return lists;
    }

    // リスト追加
    public async Task<WatchList> CreateAsync(string name)
    {
        var lists = await GetAllAsync();
        var newList = new WatchList { Name = name };
        lists.Add(newList);
        await SaveAllAsync(lists);
        return newList;
    }

    // リスト名変更
    public async Task RenameAsync(string id, string newName)
    {
        var lists = await GetAllAsync();
        var target = lists.FirstOrDefault(l => l.Id == id);
        if (target == null) return;
        target.Name = newName;
        await SaveAllAsync(lists);
    }

    // リスト削除（孤立銘柄もマスターから削除）
    public async Task DeleteAsync(string id)
    {
        var lists = await GetAllAsync();
        var target = lists.FirstOrDefault(l => l.Id == id);
        if (target == null) return;

        // このリストのみに属する銘柄を特定
        var otherCodes = lists
            .Where(l => l.Id != id)
            .SelectMany(l => l.StockCodes)
            .ToHashSet();
        var orphanCodes = target.StockCodes.Where(c => !otherCodes.Contains(c)).ToList();

        // 孤立銘柄をマスターから削除
        if (orphanCodes.Count > 0)
        {
            var stocks = await stockService.GetAllAsync();
            stocks.RemoveAll(s => orphanCodes.Contains(s.Code));
            await stockService.SaveAsync(stocks);
        }

        lists.Remove(target);
        await SaveAllAsync(lists);
    }

    // 銘柄をリストに追加
    public async Task AddStockAsync(string listId, string code)
    {
        var lists = await GetAllAsync();
        var target = lists.FirstOrDefault(l => l.Id == listId);
        if (target == null || target.StockCodes.Contains(code)) return;
        target.StockCodes.Add(code);
        await SaveAllAsync(lists);
    }

    // 銘柄をリストから除外（最後のリストの場合はマスターからも削除）
    public async Task RemoveStockAsync(string listId, string code)
    {
        var lists = await GetAllAsync();
        var target = lists.FirstOrDefault(l => l.Id == listId);
        if (target == null) return;
        target.StockCodes.Remove(code);

        var isOrphan = lists.All(l => !l.StockCodes.Contains(code));
        if (isOrphan)
        {
            var stocks = await stockService.GetAllAsync();
            stocks.RemoveAll(s => s.Code == code);
            await stockService.SaveAsync(stocks);
        }

        await SaveAllAsync(lists);
    }

    // 銘柄が他のリストに存在するか確認
    public async Task<bool> ExistsInOtherListAsync(string currentListId, string code)
    {
        var lists = await GetAllAsync();
        return lists.Any(l => l.Id != currentListId && l.StockCodes.Contains(code));
    }

    // リスト内の並び順を保存
    public async Task UpdateOrderAsync(string listId, List<string> orderedCodes)
    {
        var lists = await GetAllAsync();
        var target = lists.FirstOrDefault(l => l.Id == listId);
        if (target == null) return;
        target.StockCodes = orderedCodes;
        await SaveAllAsync(lists);
    }

    // リストのみに属する銘柄数（削除確認用）
    public async Task<int> GetOrphanCountAsync(string listId)
    {
        var lists = await GetAllAsync();
        var target = lists.FirstOrDefault(l => l.Id == listId);
        if (target == null) return 0;
        var otherCodes = lists
            .Where(l => l.Id != listId)
            .SelectMany(l => l.StockCodes)
            .ToHashSet();
        return target.StockCodes.Count(c => !otherCodes.Contains(c));
    }
}
