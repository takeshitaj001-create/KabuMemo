using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KabuMemo.Models;

namespace KabuMemo.Services;

public class StockApiService(HttpClient http)
{
    // Cloudflare Worker をデプロイ後、この URL を更新してください
    // 例: "https://kabumemo-proxy.your-name.workers.dev"
    
    //private const string ProxyBaseUrl = "https://kabumemo-proxy.YOUR_SUBDOMAIN.workers.dev"; // 本番用
    private const string ProxyBaseUrl = "http://localhost:8787"; //ローカルテスト用


    // ---- 銘柄和名取得 ----

    public async Task<string?> FetchJapaneseNameAsync(string code)
    {
        try
        {
            var resp = await http.GetFromJsonAsync<YahooSearchResponse>(
                $"{ProxyBaseUrl}/api/name/{Uri.EscapeDataString(code)}");
            var quote = resp?.Quotes?.FirstOrDefault(q =>
                q.Symbol?.StartsWith(code, StringComparison.OrdinalIgnoreCase) == true
                || q.Symbol?.Contains(code, StringComparison.OrdinalIgnoreCase) == true);
            return quote?.LongName ?? quote?.ShortName;
        }
        catch
        {
            return null;
        }
    }

    // ---- 株価取得（Yahoo Finance v7 via Cloudflare Worker） ----

    public async Task<QuoteResult> FetchQuoteAsync(string code)
    {
        try
        {
            var resp = await http.GetFromJsonAsync<YahooV7Response>(
                $"{ProxyBaseUrl}/api/quote/{Uri.EscapeDataString(code)}");

            if (resp?.QuoteResponse?.Error != null)
                return QuoteResult.Fail(code, $"APIエラー: {resp.QuoteResponse.Error}");

            var q = resp?.QuoteResponse?.Result?.FirstOrDefault();
            if (q == null)
                return QuoteResult.Fail(code, "レスポンスにデータがありません（市場休場中の可能性）");
            if (q.RegularMarketPrice <= 0)
                return QuoteResult.Fail(code, $"株価が 0 以下です（{code}.T）。銘柄コードを確認してください");

            return QuoteResult.Ok(new StockQuote
            {
                Code = code,
                Close = q.RegularMarketPrice,
                PreviousClose = q.RegularMarketPreviousClose,
                Change = q.RegularMarketChange,
                ChangeRate = q.RegularMarketChangePercent,
                Open = q.RegularMarketOpen,
                High = q.RegularMarketDayHigh,
                Low = q.RegularMarketDayLow,
                Volume = q.RegularMarketVolume,
                LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Exchange = q.FullExchangeName,
                MarketCap = q.MarketCap,
                LotSize = q.LotSize,
                ForwardPE = q.ForwardPE,
                PriceToBook = q.PriceToBook,
                EPS = q.EpsTrailingTwelveMonths,
                BPS = q.BookValue,
                DividendYield = q.TrailingAnnualDividendYield,
                DividendRate = q.TrailingAnnualDividendRate,
                WeekHigh52 = q.FiftyTwoWeekHigh,
                WeekLow52 = q.FiftyTwoWeekLow,
            });
        }
        catch (HttpRequestException ex)
        {
            var status = ex.StatusCode.HasValue ? $" (HTTP {(int)ex.StatusCode})" : "";
            return QuoteResult.Fail(code, $"HTTP エラー{status}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return QuoteResult.Fail(code, "タイムアウト（10秒）");
        }
        catch (Exception ex)
        {
            return QuoteResult.Fail(code, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- 配当推移取得（Yahoo Finance v8 chart API） ----

    public async Task<DividendResult> FetchDividendsAsync(string code)
    {
        try
        {
            var resp = await http.GetFromJsonAsync<List<DividendEntry>>(
                $"{ProxyBaseUrl}/api/dividends/{Uri.EscapeDataString(code)}");
            if (resp == null)
                return DividendResult.Fail("レスポンスが空です");
            return DividendResult.Ok(resp);
        }
        catch (HttpRequestException ex)
        {
            var status = ex.StatusCode.HasValue ? $" (HTTP {(int)ex.StatusCode})" : "";
            return DividendResult.Fail($"HTTP エラー{status}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return DividendResult.Fail("タイムアウト");
        }
        catch (Exception ex)
        {
            return DividendResult.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- 企業概要取得（Yahoo Finance quoteSummary + CF Workers AI 翻訳） ----

    public async Task<SummaryResult> FetchSummaryAsync(string code)
    {
        try
        {
            var resp = await http.GetFromJsonAsync<CompanySummaryResponse>(
                $"{ProxyBaseUrl}/api/summary/{Uri.EscapeDataString(code)}");
            if (resp == null)
                return SummaryResult.Fail("レスポンスが空です");
            return SummaryResult.Ok(resp);
        }
        catch (HttpRequestException ex)
        {
            var status = ex.StatusCode.HasValue ? $" (HTTP {(int)ex.StatusCode})" : "";
            return SummaryResult.Fail($"HTTP エラー{status}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return SummaryResult.Fail("タイムアウト");
        }
        catch (Exception ex)
        {
            return SummaryResult.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- 適時開示データ取得（irbank.net via Cloudflare Worker） ----

    public async Task<IrFetchResult> FetchTdnetAsync(string code)
    {
        try
        {
            var html = await http.GetStringAsync(
                $"{ProxyBaseUrl}/api/tdnet/{Uri.EscapeDataString(code)}");
            return IrFetchResult.Ok(ParseTdnet(html));
        }
        catch (HttpRequestException ex)
        {
            var status = ex.StatusCode.HasValue ? $" (HTTP {(int)ex.StatusCode})" : "";
            return IrFetchResult.Fail($"HTTP エラー{status}: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return IrFetchResult.Fail("タイムアウト（irbank.net）");
        }
        catch (Exception ex)
        {
            return IrFetchResult.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static List<IrInfo> ParseTdnet(string html)
    {
        var results = new List<IrInfo>();
        var tbodyMatch = Regex.Match(html, @"<tbody[^>]*>(.*?)</tbody>", RegexOptions.Singleline);
        var body = tbodyMatch.Success ? tbodyMatch.Groups[1].Value : html;

        foreach (Match tr in Regex.Matches(body, @"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline))
        {
            var tds = Regex.Matches(tr.Groups[1].Value, @"<td[^>]*>(.*?)</td>", RegexOptions.Singleline);
            if (tds.Count < 3) continue;

            var date = StripHtml(tds[0].Groups[1].Value);
            var category = StripHtml(tds[1].Groups[1].Value);

            var linkMatch = Regex.Match(tds[2].Groups[1].Value,
                @"<a[^>]+href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Singleline);
            if (!linkMatch.Success) continue;

            var title = WebUtility.HtmlDecode(StripHtml(linkMatch.Groups[2].Value));
            var href = linkMatch.Groups[1].Value;
            var irUrl = href.StartsWith("http") ? href : "https://irbank.net" + href;

            if (Regex.IsMatch(date, @"\d{4}") && title.Length > 0)
                results.Add(new IrInfo { Date = date, Category = category, Title = title, Url = irUrl });
        }
        return results;
    }

    private static string StripHtml(string html) =>
        Regex.Replace(html, @"<[^>]+>", "").Trim();
}

// ---- 適時開示取得結果ラッパー ----

public class IrFetchResult
{
    public List<IrInfo>? Items { get; private set; }
    public string? Error { get; private set; }
    public bool Success => Items != null;

    public static IrFetchResult Ok(List<IrInfo> items) => new() { Items = items };
    public static IrFetchResult Fail(string msg) => new() { Error = msg };
}

// ---- 株価取得結果ラッパー ----

public class QuoteResult
{
    public StockQuote? Quote { get; private set; }
    public string? Error { get; private set; }
    public bool Success => Quote != null;

    public static QuoteResult Ok(StockQuote q) => new() { Quote = q };
    public static QuoteResult Fail(string code, string msg) => new() { Error = $"[{code}] {msg}" };
}

// ---- Yahoo Finance Search レスポンスモデル ----

public class YahooSearchResponse
{
    [JsonPropertyName("quotes")]
    public List<YahooSearchQuote>? Quotes { get; set; }
}

public class YahooSearchQuote
{
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("longname")]
    public string? LongName { get; set; }

    [JsonPropertyName("shortname")]
    public string? ShortName { get; set; }

    [JsonPropertyName("exchDisp")]
    public string? ExchDisp { get; set; }
}

// ---- Yahoo Finance v7 レスポンスモデル ----

public class YahooV7Response
{
    [JsonPropertyName("quoteResponse")]
    public YahooV7QuoteResponse? QuoteResponse { get; set; }
}

public class YahooV7QuoteResponse
{
    [JsonPropertyName("result")]
    public List<YahooV7Quote>? Result { get; set; }

    [JsonPropertyName("error")]
    public object? Error { get; set; }
}

public class YahooV7Quote
{
    [JsonPropertyName("regularMarketPrice")]
    public decimal RegularMarketPrice { get; set; }

    [JsonPropertyName("regularMarketChange")]
    public decimal RegularMarketChange { get; set; }

    [JsonPropertyName("regularMarketChangePercent")]
    public decimal RegularMarketChangePercent { get; set; }

    [JsonPropertyName("regularMarketOpen")]
    public decimal RegularMarketOpen { get; set; }

    [JsonPropertyName("regularMarketDayHigh")]
    public decimal RegularMarketDayHigh { get; set; }

    [JsonPropertyName("regularMarketDayLow")]
    public decimal RegularMarketDayLow { get; set; }

    [JsonPropertyName("regularMarketPreviousClose")]
    public decimal RegularMarketPreviousClose { get; set; }

    [JsonPropertyName("regularMarketVolume")]
    public long RegularMarketVolume { get; set; }

    [JsonPropertyName("fullExchangeName")]
    public string? FullExchangeName { get; set; }

    [JsonPropertyName("marketCap")]
    public long? MarketCap { get; set; }

    [JsonPropertyName("lotSize")]
    public int? LotSize { get; set; }

    [JsonPropertyName("forwardPE")]
    public decimal? ForwardPE { get; set; }

    [JsonPropertyName("priceToBook")]
    public decimal? PriceToBook { get; set; }

    [JsonPropertyName("epsTrailingTwelveMonths")]
    public decimal? EpsTrailingTwelveMonths { get; set; }

    [JsonPropertyName("bookValue")]
    public decimal? BookValue { get; set; }

    [JsonPropertyName("trailingAnnualDividendYield")]
    public decimal? TrailingAnnualDividendYield { get; set; }

    [JsonPropertyName("trailingAnnualDividendRate")]
    public decimal? TrailingAnnualDividendRate { get; set; }

    [JsonPropertyName("fiftyTwoWeekHigh")]
    public decimal? FiftyTwoWeekHigh { get; set; }

    [JsonPropertyName("fiftyTwoWeekLow")]
    public decimal? FiftyTwoWeekLow { get; set; }
}

// ---- 株価取得結果モデル ----

public class StockQuote
{
    public string Code { get; set; } = "";
    public decimal Close { get; set; }
    public decimal PreviousClose { get; set; }
    public decimal Change { get; set; }
    public decimal ChangeRate { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public long Volume { get; set; }
    public string LastUpdated { get; set; } = "";

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

    public string Direction => Change > 0 ? "up" : Change < 0 ? "down" : "flat";
}

// ---- 企業概要取得結果ラッパー ----

public class SummaryResult
{
    public CompanySummaryResponse? Data { get; private set; }
    public string? Error { get; private set; }
    public bool Success => Data != null;

    public static SummaryResult Ok(CompanySummaryResponse data) => new() { Data = data };
    public static SummaryResult Fail(string msg) => new() { Error = msg };
}

// ---- 配当取得結果ラッパー ----

public class DividendResult
{
    public List<DividendEntry>? Items { get; private set; }
    public string? Error { get; private set; }
    public bool Success => Items != null;

    public static DividendResult Ok(List<DividendEntry> items) => new() { Items = items };
    public static DividendResult Fail(string msg) => new() { Error = msg };
}

// ---- 企業概要レスポンスモデル ----

public class CompanySummaryResponse
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("employees")]
    public int? Employees { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }
}
