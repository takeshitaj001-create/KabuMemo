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

    // 基本情報・参考指標
    public string? Exchange { get; set; }
    public long? MarketCap { get; set; }
    public int? LotSize { get; set; }
    public decimal? ForwardPE { get; set; }
    public decimal? PriceToBook { get; set; }
    public decimal? EPS { get; set; }
    public decimal? BPS { get; set; }
    public decimal? DividendYield { get; set; }
    public decimal? DividendRate { get; set; }
    public decimal? WeekHigh52 { get; set; }
    public decimal? WeekLow52 { get; set; }
}
