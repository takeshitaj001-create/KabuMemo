namespace KabuMemo.Models;

public class WatchList
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public List<string> StockCodes { get; set; } = [];
}
