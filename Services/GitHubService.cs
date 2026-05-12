using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;

namespace KabuMemo.Services;

public record GitHubFileInfo(string Name, string Sha, string DownloadUrl, string Path);

public class GitHubService(IJSRuntime js)
{
    private const string SettingsKey = "kabumemo_github_pat";
    private const string DefaultPat = "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";
    public const string Owner = "takeshitaj001-create";
    public const string Repo = "KabuMemo-shared";
    public const string Folder = "shared-md";

    private static readonly HttpClient Http = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<string?> GetPatAsync()
    {
        var stored = await js.InvokeAsync<string?>("localStorage.getItem", SettingsKey);
        if (!string.IsNullOrEmpty(stored)) return stored;
        return DefaultPat;
    }

    public async Task<string?> GetStoredPatAsync()
    {
        return await js.InvokeAsync<string?>("localStorage.getItem", SettingsKey);
    }

    public async Task SavePatAsync(string pat)
    {
        await js.InvokeVoidAsync("localStorage.setItem", SettingsKey, pat);
    }

    public async Task DeletePatAsync()
    {
        await js.InvokeVoidAsync("localStorage.removeItem", SettingsKey);
    }

    public async Task<(List<GitHubFileInfo> Files, string? Error)> ListFilesWithPatAsync(string pat)
    {
        if (string.IsNullOrEmpty(pat)) return ([], "PATが入力されていません。");
        return await FetchFileListAsync(pat);
    }

    public async Task<(List<GitHubFileInfo> Files, string? Error)> ListFilesAsync()
    {
        var pat = await GetPatAsync();
        if (string.IsNullOrEmpty(pat)) return ([], "PATが未登録です。設定画面でPersonal Access Tokenを設定してください。");
        return await FetchFileListAsync(pat);
    }

    private static async Task<(List<GitHubFileInfo> Files, string? Error)> FetchFileListAsync(string pat)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{Owner}/{Repo}/contents/{Folder}");
        AddHeaders(req, pat);

        try
        {
            var res = await Http.SendAsync(req);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                return ([], null);
            if (!res.IsSuccessStatusCode)
                return ([], $"GitHub API エラー: {(int)res.StatusCode}");

            var body = await res.Content.ReadAsStringAsync();
            var items = JsonSerializer.Deserialize<List<GhContentItem>>(body, JsonOpts) ?? [];

            var files = items
                .Where(i => i.Type == "file" && i.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .Select(i => new GitHubFileInfo(i.Name, i.Sha, i.Download_Url, i.Path))
                .OrderByDescending(i => i.Name)
                .ToList();

            return (files, null);
        }
        catch (Exception ex)
        {
            return ([], $"通信エラー: {ex.Message}");
        }
    }

    public async Task<string?> UploadFileAsync(string filename, string content)
    {
        var pat = await GetPatAsync();
        if (string.IsNullOrEmpty(pat)) return "PATが未登録です。";

        var (files, _) = await ListFilesAsync();
        var existingSha = files.FirstOrDefault(f => f.Name == filename)?.Sha;

        var path = $"{Folder}/{filename}";
        using var req = new HttpRequestMessage(
            HttpMethod.Put,
            $"https://api.github.com/repos/{Owner}/{Repo}/contents/{path}");
        AddHeaders(req, pat);

        var bodyObj = new Dictionary<string, string>
        {
            ["message"] = $"Upload MD: {filename}",
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content))
        };
        if (existingSha != null) bodyObj["sha"] = existingSha;

        req.Content = new StringContent(
            JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json");

        try
        {
            var res = await Http.SendAsync(req);
            return res.IsSuccessStatusCode ? null : $"アップロード失敗: {(int)res.StatusCode}";
        }
        catch (Exception ex)
        {
            return $"通信エラー: {ex.Message}";
        }
    }

    public async Task<(string? Content, string? Error)> DownloadFileAsync(string path)
    {
        var pat = await GetPatAsync();
        if (string.IsNullOrEmpty(pat)) return (null, "PATが未登録です。");

        using var req = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{Owner}/{Repo}/contents/{path}");
        AddHeaders(req, pat);

        try
        {
            var res = await Http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
                return (null, $"ダウンロード失敗: {(int)res.StatusCode}");

            var body = await res.Content.ReadAsStringAsync();
            var item = JsonSerializer.Deserialize<GhContentItem>(body, JsonOpts);
            if (item?.Content == null)
                return (null, "コンテンツの取得に失敗しました。");

            var base64 = item.Content.Replace("\n", "").Replace("\r", "");
            var bytes = Convert.FromBase64String(base64);
            return (Encoding.UTF8.GetString(bytes), null);
        }
        catch (Exception ex)
        {
            return (null, $"通信エラー: {ex.Message}");
        }
    }

    private static void AddHeaders(HttpRequestMessage req, string pat)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", pat);
        req.Headers.UserAgent.ParseAdd("KabuMemo");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    private class GhContentItem
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Sha { get; set; } = "";
        public string Path { get; set; } = "";
        public string Download_Url { get; set; } = "";
        public string? Content { get; set; }
    }
}
