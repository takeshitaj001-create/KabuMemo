# Cloudflare Worker デプロイ手順

KabuMemo の株価取得・IR情報取得は、CORS 制約を回避するために Cloudflare Worker をプロキシとして使用します。

## 概要

```
ブラウザ（Blazor WASM）
    ↓  CORS なし
Cloudflare Worker（kabumemo-proxy）
    ├─ Cookie+Crumb 認証 → Yahoo Finance API（株価・チャート・配当・企業概要）
    ├─ スクレイピング   → Yahoo Finance Japan（銘柄名検索）
    ├─ 翻訳            → MyMemory API（企業概要を日本語に翻訳）
    └─ スクレイピング   → irbank.net（適時開示）
```

## 事前準備

### Cloudflare アカウント（未登録の場合）

https://dash.cloudflare.com/sign-up で無料アカウントを作成してください。

> **料金について**  
> Cloudflare Workers の無料枠は 1日あたり 10 万リクエストまで。個人利用であれば追加料金は不要です。

## デプロイ手順

### 1. wrangler のインストール

```powershell
cd worker
npm install
```

### 2. Cloudflare にログイン

```powershell
npx wrangler login
```

ブラウザが自動で開くので、Cloudflare アカウントでログインして認証します。

### 3. デプロイ実行

```powershell
npx wrangler deploy
```

成功すると以下のように URL が表示されます。

```
✅ Deployed kabumemo-proxy
   https://kabumemo-proxy.XXXX.workers.dev
```

この URL をコピーしてください（`XXXX` の部分は Cloudflare アカウントのサブドメインで、デプロイ時に確定します）。

### 4. URL を設定ファイルに反映

`Services/StockApiService.cs` の 14 行目を、デプロイで表示された URL に書き換えます。

```csharp
// 変更前
private const string ProxyBaseUrl = "https://kabumemo-proxy.YOUR_SUBDOMAIN.workers.dev";

// 変更後（例）
private const string ProxyBaseUrl = "https://kabumemo-proxy.kabumemo.workers.dev";
```

### 5. Blazor アプリを再ビルド・デプロイ

```powershell
dotnet publish -c Release
```

## ローカルでのテスト

Worker と Blazor アプリを別々に起動して動作確認できます。

```powershell
# ターミナル①: Worker をローカル起動（http://localhost:8787）
cd worker
npx wrangler dev

# ターミナル②: Blazor アプリを起動（http://localhost:5124）
cd ..
dotnet run
```

ローカルテスト中は `StockApiService.cs` の URL を以下に変更してください。

```csharp
private const string ProxyBaseUrl = "http://localhost:8787";
```

テスト完了後は本番 URL に戻してから再デプロイします。

## Worker のエンドポイント

| エンドポイント | 用途 |
|---|---|
| `GET /api/quote/{code}` | 株価取得（Yahoo Finance v7、v8フォールバックあり） |
| `GET /api/chart/{code}` | 株価チャート取得（Yahoo Finance v8、1年間の日次データ） |
| `GET /api/dividends/{code}` | 配当推移取得（Yahoo Finance v8、過去5年・年度集計） |
| `GET /api/summary/{code}` | 企業概要取得（Yahoo Finance quoteSummary + MyMemory 日本語翻訳） |
| `GET /api/tdnet/{code}` | 適時開示取得（irbank.net スクレイピング） |
| `GET /api/name/{code}` | 銘柄名・コード検索（Yahoo Finance Japan / US Search） |

## 更新・再デプロイ

Worker のコードを変更した場合は再度 `wrangler deploy` を実行するだけです。

```powershell
cd worker
npx wrangler deploy
```
