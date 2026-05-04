namespace KabuMemo.Models;

public class StockItem
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Memo { get; set; } = "";
    public string? AlarmDate { get; set; }
    public bool IsBuyCandidate { get; set; }
    public bool IsHolding { get; set; }

    public decimal? CurrentPrice { get; set; }
    public decimal? PreviousClose { get; set; }
    public decimal? PriceChange { get; set; }
    public decimal? PriceChangeRate { get; set; }
    public string PriceDirection { get; set; } = "flat";

    public decimal? OpenPrice { get; set; }
    public decimal? HighPrice { get; set; }
    public decimal? LowPrice { get; set; }
    public long? Volume { get; set; }

    public string? LastUpdated { get; set; }
}
