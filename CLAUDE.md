# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**KabuMemo** — 株式投資に関するメモを管理する Blazor WebAssembly アプリ。GitHub Pages で動作する静的 SPA。

## コマンド

```bash
# 開発サーバー起動（http://localhost:5124）
dotnet run

# ホットリロード付き開発
dotnet watch

# GitHub Pages 向けリリースビルド
dotnet publish -c Release
# 出力先: bin/Release/net10.0/publish/wwwroot/
```

テストプロジェクトは未実装。

## アーキテクチャ

Blazor WebAssembly（.NET 10）。サーバーサイド処理なし。データ永続化は `localStorage`（JS Interop 経由）のみ。

**ルーティング:** `App.razor` がルートを定義。`Pages/` 配下の `.razor` ファイルが各画面。

**状態管理:** `localStorage` を `IJSRuntime` で読み書き。銘柄一覧など永続化が必要なデータはすべてここに保存する。

**外部 API:** `HttpClient` で呼び出す（Yahoo Finance など）。`Program.cs` でサービス登録済み。CORS 制約あり。

**スタイリング:** Bootstrap 5（`wwwroot/lib/bootstrap/`）+ `wwwroot/css/app.css`。

## 画面構成

| 画面 | ファイル | 役割 |
|---|---|---|
| 一覧画面（メイン） | `Pages/Home.razor` | 銘柄一覧、アラーム日付、ハイライト表示 |
| 個別銘柄画面 | 未実装 | 詳細情報・IR 情報一覧 |
| Yahoo Finance 取得 | 未実装 | 外部データ取得処理 |

## 機能仕様

### 一覧画面
- 銘柄ごとにメモを一覧管理
- 登録済み銘柄を `localStorage` で永続化
- 各行にアラーム日付（カレンダー）を設定
- 日付が本日以前の行をハイライト
- 銘柄行タップで個別画面へ遷移

### 個別銘柄画面
- 一覧と同じ情報＋ IR 情報一覧

## GitHub Pages デプロイ

```bash
dotnet publish -c Release
# publish/wwwroot/index.html を 404.html としてコピー（SPA ルーティング対策）
```

`wwwroot/index.html` の `<base>` タグをリポジトリ名に合わせること：
```html
<base href="/KabuMemo/" />
```

CI は `.github/workflows/deploy.yml` で自動デプロイ。リポジトリの Settings → Pages で `gh-pages` ブランチを Source に設定。

## localStorage パターン

```csharp
@inject IJSRuntime JS

await JS.InvokeVoidAsync("localStorage.setItem", "key", value);
var value = await JS.InvokeAsync<string>("localStorage.getItem", "key");
```
