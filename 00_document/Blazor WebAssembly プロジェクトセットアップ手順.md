# Blazor WebAssembly プロジェクトセットアップ手順

## 前提条件

| ツール | バージョン | 確認コマンド |
|---|---|---|
| .NET SDK | 10.0 以上推奨 | `dotnet --version` |
| Git | 任意 | `git --version` |
| VS Code または Visual Studio | 任意 | — |

### .NET SDK インストール

https://dotnet.microsoft.com/download からインストーラーをダウンロードして実行。

---

## 1. プロジェクト作成

```bash
dotnet new blazorwasm -o KabuMemo
cd KabuMemo
```

### 主なオプション

| オプション | 説明 |
|---|---|
| `-o <名前>` | 出力フォルダ名（プロジェクト名）|
| `--pwa` | PWA（オフライン対応）を有効化 |
| `--hosted` | ASP.NET Core バックエンド付き構成（GitHub Pages では不要）|

---

## 2. ローカル開発サーバーの起動

```bash
dotnet run
```

ブラウザで `http://localhost:5124`（または表示された URL）を開く。

ホットリロードを使う場合:

```bash
dotnet watch
```

---

## 3. プロジェクト構成

```
KabuMemo/
├── wwwroot/               # 静的ファイル（画像・CSS・index.html）
│   ├── index.html         # エントリーポイント
│   ├── css/               # スタイルシート
│   └── lib/               # Bootstrap などのライブラリ
├── Pages/                 # 各画面の .razor ファイル
│   ├── Home.razor
│   ├── StockDetail.razor
│   └── NotFound.razor
├── Layout/                # レイアウト・ナビゲーション
│   ├── MainLayout.razor
│   └── NavMenu.razor
├── App.razor              # ルーティング定義
├── _Imports.razor         # グローバル using 宣言
└── Program.cs             # DI・起動設定
```

> .NET 10 テンプレートでは `Shared/` フォルダが `Layout/` に変更された。

---

## 4. サービス登録（Program.cs）

```csharp
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// デフォルト HttpClient（ベースアドレスはホスト環境に合わせて自動設定）
builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// 独自サービスの登録例
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped(_ => new StockApiService(
    new HttpClient { Timeout = TimeSpan.FromSeconds(10) }
));

await builder.Build().RunAsync();
```

---

## 5. GitHub Pages 向けビルド

### 5-1. base href の扱い

開発時は `wwwroot/index.html` の `<base>` タグを `/` のままにしておく。
GitHub Pages 向けの `base href` 書き換えは **CI（deploy.yml）側で自動実行**するため、
手動での変更は不要。

```html
<!-- ローカル開発・ソース管理時はこのまま -->
<base href="/" />
```

### 5-2. リリースビルド

```bash
dotnet publish -c Release
```

出力先: `bin/Release/net10.0/publish/wwwroot/`

### 5-3. 404 対策（GitHub Pages 用）

GitHub Pages は SPA のルーティングに対応していないため、`wwwroot/` に `404.html` を追加する。
CI の `deploy.yml` でコピーしている場合は手動対応不要。

```bash
# index.html を 404.html としてコピー（手動の場合）
cp bin/Release/net10.0/publish/wwwroot/index.html bin/Release/net10.0/publish/wwwroot/404.html
```

---

## 6. GitHub Actions による自動デプロイ

`.github/workflows/deploy.yml` の構成（本プロジェクト実績）:

```yaml
name: Deploy to GitHub Pages

on:
  push:
    branches:
      - master

permissions:
  contents: read
  pages: write
  id-token: write

concurrency:
  group: pages
  cancel-in-progress: false

env:
  FORCE_JAVASCRIPT_ACTIONS_TO_NODE24: true

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'

      - name: Publish
        run: dotnet publish -c Release

      - name: Set base href for GitHub Pages
        run: |
          sed -i 's|<base href="[^"]*"|<base href="/KabuMemo/"|g' \
            bin/Release/net10.0/publish/wwwroot/index.html
          grep 'base href' bin/Release/net10.0/publish/wwwroot/index.html

      - name: Upload Pages artifact
        uses: actions/upload-pages-artifact@v3
        with:
          path: bin/Release/net10.0/publish/wwwroot

  deploy:
    needs: build
    runs-on: ubuntu-latest
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    steps:
      - name: Deploy to GitHub Pages
        id: deployment
        uses: actions/deploy-pages@v4
```

### GitHub リポジトリ側の設定

1. リポジトリの **Settings → Pages**
2. Source を **GitHub Actions** に設定（`gh-pages` ブランチではなく Actions を使う）

---

## 7. 外部 API の呼び出し（HttpClient）

CORS 制約があるため、呼び出し可能な API は限られる。
外部 API 専用の `HttpClient` を登録する場合は `Program.cs` で直接インスタンスを渡す。

```csharp
// Program.cs
builder.Services.AddScoped(_ => new StockApiService(
    new HttpClient { Timeout = TimeSpan.FromSeconds(10) }
));
```

コンポーネントからの呼び出し例:

```csharp
@inject StockApiService StockApi

@code {
    protected override async Task OnInitializedAsync()
    {
        var data = await StockApi.GetDailyBarsAsync("13010");
    }
}
```

---

## 8. localStorage の利用

JavaScript Interop を使って `localStorage` にアクセスする。

```csharp
@inject IJSRuntime JS

@code {
    // 保存
    await JS.InvokeVoidAsync("localStorage.setItem", "key", value);

    // 読み込み
    var value = await JS.InvokeAsync<string>("localStorage.getItem", "key");
}
```

---

## 参考リンク

- [Blazor 公式ドキュメント](https://learn.microsoft.com/ja-jp/aspnet/core/blazor/)
- [Blazor WebAssembly を GitHub Pages にデプロイ](https://learn.microsoft.com/ja-jp/aspnet/core/blazor/host-and-deploy/webassembly)
