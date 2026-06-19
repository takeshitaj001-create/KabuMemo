using KabuMemo.Models;

namespace KabuMemo.Services;

public class WatchListStateService
{
    public WatchList? ActiveList { get; private set; }
    public List<WatchList> AllLists { get; private set; } = [];

    public event Action? OnChange;

    public void SetLists(List<WatchList> lists, string? activeId)
    {
        AllLists = lists;
        ActiveList = lists.FirstOrDefault(l => l.Id == activeId) ?? lists.FirstOrDefault();
        NotifyStateChanged();
    }

    public void SetActiveList(WatchList list)
    {
        ActiveList = list;
        NotifyStateChanged();
    }

    public void RefreshLists(List<WatchList> lists)
    {
        var currentId = ActiveList?.Id;
        AllLists = lists;
        ActiveList = lists.FirstOrDefault(l => l.Id == currentId) ?? lists.FirstOrDefault();
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}
