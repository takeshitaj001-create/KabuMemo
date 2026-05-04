# Blazor WebAssembly プロジェクトセットアップ手順

## 前提条件

| ツール | バージョン | 確認コマンド |
|---|---|---|
| .NET SDK | 8.0 以上推奨 | `dotnet --version` |
| Git | 任意 | `git --version` |
| VS Code または Visual Studio | 任意 | — |

### .NET SDK インストール
https://dotnet.microsoft.com/download からインストーラーをダウンロードして実行。

---

## 1. プロジェクト作成

```bash
dotnet new blazorwasm -o KabuMemoWeb
cd KabuMemoWeb
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

ブラウザで `https://localhost:5124`（または表示された URL）を開く。

ホットリロードを使う場合:

```bash
dotnet watch
```

---

## 3. プロジェクト構成

```
KabuMemoWeb/
├── wwwroot/               # 静的ファイル（画像・CSS・index.html）
│   └── index.html         # エントリーポイント
├── Pages/                 # 各画面の .razor ファイル
│   ├── Home.razor
│   └── Counter.razor
├── Shared/                # 共通コンポーネント
│   └── MainLayout.razor
├── App.razor              # ルーティング定義
└── Program.cs             # DI・起動設定
```

---

## 4. GitHub Pages 向けビルド

### 4-1. base href の修正

`wwwroot/index.html` の `<base>` タグをリポジトリ名に合わせて変更する。

```html
<!-- 変更前 -->
<base href="/" />

<!-- 変更後（リポジトリ名: KabuMemoWeb の場合） -->
<base href="/KabuMemoWeb/" />
```

### 4-2. リリースビルド

```bash
dotnet publish -c Release
```

出力先: `bin/Release/net8.0/publish/wwwroot/`

### 4-3. 404 対策（GitHub Pages 用）

GitHub Pages は SPA のルーティングに対応していないため、`wwwroot/` に `404.html` を追加する。

```bash
# index.html を 404.html としてコピー
cp wwwroot/index.html wwwroot/404.html
```

---

## 5. GitHub Actions による自動デプロイ

`.github/workflows/deploy.yml` を作成する。

```yaml
name: Deploy to GitHub Pages

on:
  push:
    branches:
      - main

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Publish
        run: dotnet publish -c Release -o publish

      - name: Copy 404.html
        run: cp publish/wwwroot/index.html publish/wwwroot/404.html

      - name: Deploy
        uses: peaceiris/actions-gh-pages@v4
        with:
          github_token: ${{ secrets.GITHUB_TOKEN }}
          publish_dir: publish/wwwroot
```

### GitHub リポジトリ側の設定

1. リポジトリの **Settings → Pages**
2. Source を `gh-pages` ブランチに設定

---

## 6. 外部 API の呼び出し（HttpClient）

`Program.cs` に HttpClient を登録する。

```csharp
builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri("https://api.example.com") });
```

コンポーネントから呼び出す例（J-Quants API など）:

```csharp
@inject HttpClient Http

@code {
    protected override async Task OnInitializedAsync()
    {
        var result = await Http.GetFromJsonAsync<MyData>("/v2/equities/bars/daily?code=13010");
    }
}
```

---

## 7. localStorage の利用

JavaScript Interop を使って `localStorage` にアクセスする。

```csharp
@inject IJSRuntime JS

@code {
    await JS.InvokeVoidAsync("localStorage.setItem", "apiKey", value);
    var apiKey = await JS.InvokeAsync<string>("localStorage.getItem", "apiKey");
}
```

---

## 参考リンク

- [Blazor 公式ドキュメント](https://learn.microsoft.com/ja-jp/aspnet/core/blazor/)
- [Blazor WebAssembly を GitHub Pages にデプロイ](https://learn.microsoft.com/ja-jp/aspnet/core/blazor/host-and-deploy/webassembly)
