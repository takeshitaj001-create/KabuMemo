# KabuMemo 実装完了まとめ v1.0

## 概要

`doc/mockup_kabumemo_v1.0.html` のモックアップに基づき、Blazor WebAssembly（.NET 10）で株式投資メモ管理アプリを実装した。GitHub Pages で動作するサーバーレス SPA。

---

## 作成・変更ファイル一覧

| ファイル | 変更種別 | 概要 |
|---|---|---|
| `Models/StockItem.cs` | 新規作成 | 銘柄データモデル |
| `Services/StockService.cs` | 新規作成 | localStorage CRUD サービス |
| `Services/StockApiService.cs` | 新規作成 | 株銘柄和名取得・株価リアルタイム取得 |
| `Program.cs` | 修正 | `StockService`・`StockApiService` を DI 登録 |
| `_Imports.razor` | 修正 | `using` 2行追加 |
| `Layout/MainLayout.razor` | 修正 | サイドバー削除、固定ヘッダー追加 |
| `Layout/MainLayout.razor.css` | 修正 | サイドバー関連スタイルを削除 |
| `Pages/Home.razor` | 全面書き換え | 銘柄一覧画面 |
| `Pages/StockDetail.razor` | 新規作成 | 個別銘柄詳細画面 |
| `wwwroot/css/app.css` | 追記 | モックアップ CSS・更新ボタンスタイルを追加 |

---

## データモデル（`Models/StockItem.cs`）

```csharp
public class StockItem
{
    public string Code { get; set; }            // 銘柄コード（例: "7203"）
    public string Name { get; set; }            // 銘柄名（例: "トヨタ自動車"）
    public string Memo { get; set; }            // メモテキスト
    public string? AlarmDate { get; set; }      // 注目日（"yyyy-MM-dd" 形式）
    public bool IsBuyCandidate { get; set; }    // 購入検討タグ
    public bool IsHolding { get; set; }         // 保有中タグ

    // 株価情報（API 取得後に更新）
    public decimal? CurrentPrice { get; set; }
    public decimal? PreviousClose { get; set; } // 前日終値
    public decimal? PriceChange { get; set; }   // 前日比（円）
    public decimal? PriceChangeRate { get; set; }// 前日比（%）
    public string PriceDirection { get; set; }  // "up" | "down" | "flat"
    public decimal? OpenPrice { get; set; }
    public decimal? HighPrice { get; set; }
    public decimal? LowPrice { get; set; }
    public long? Volume { get; set; }
    public string? LastUpdated { get; set; }    // 最終取得日時（"yyyy-MM-dd HH:mm"）
}
```

`AlarmDate` を `string?` にした理由：`<input type="date">` の `@bind` と直接マッピングでき、ISO 8601 の辞書順比較でハイライト判定が動作するため。

---

## 永続化（`Services/StockService.cs`）

- localStorage キー: `kabumemo_stocks`
- `GetAllAsync()` — `localStorage.getItem` → JSON デシリアライズ
- `SaveAsync(List<StockItem>)` — JSON シリアライズ → `localStorage.setItem`
- `System.Text.Json` を使用。後からプロパティを追加しても旧データと後方互換。

---

## 外部 API 連携（`Services/StockApiService.cs`）

### 株銘柄和名取得

| 項目 | 内容 |
|---|---|
| API | Yahoo Finance Search v1 |
| URL | `https://query1.finance.yahoo.com/v1/finance/search?q={code}&quotesCount=1&newsCount=0&lang=ja&region=JP` |
| 取得フィールド | `quotes[0].longname`（日本語銘柄名） |
| CORS | ✅ ブラウザから直接アクセス可（動作確認済み） |
| 失敗時 | `null` を返す。ユーザーが手動入力 |

呼び出し箇所：銘柄追加モーダルの「名称検索」ボタン。

### 株価リアルタイム取得

| 項目 | 内容 |
|---|---|
| API | Yahoo Finance Chart v8 |
| URL | `https://query1.finance.yahoo.com/v8/finance/chart/{code}.T?interval=1d&range=1d&region=JP&lang=ja-JP` |
| 取得フィールド | `regularMarketPrice`（現在値）、`chartPreviousClose`（前日終値）、始値・高値・安値・出来高 |
| CORS | ⚠️ ネットワーク環境依存。失敗時はエラー表示 |
| 前日比計算 | `(currentPrice − previousClose) / previousClose × 100` |
| 失敗時 | `null` を返す。画面に「⚠️ 株価取得失敗」を表示し Yahoo Finance へのリンクを案内 |

呼び出し箇所：一覧画面の「📊 株価更新」ボタン（全銘柄一括）、詳細画面の「🔄 更新」ボタン（個別）。

---

## 画面構成

### 一覧画面（`Pages/Home.razor`）

- ルート: `/`
- 銘柄追加モーダル（JS 不要、Blazor の conditional rendering で実装）
  - 「名称検索」ボタン → `StockApiService.FetchJapaneseNameAsync` を呼び、名称欄を自動入力
  - 重複コードチェック
- ツールバーに「📊 株価更新」ボタン → 全銘柄の株価を順次取得（200ms インターバル）
- 取得中は進捗カウンタ表示（`更新中 N/M …`）、取得失敗時は警告アイコン表示
- テーブル列: 詳細ボタン / 銘柄（コード+名前） / 現在値（前日比・取得時刻付き） / メモ / 注目日 / 削除
- **アラームハイライト**: 注目日が本日以前の行を黄色背景 + ⚠️ バッジで表示
- メモ・注目日は `@oninput` でリアルタイム反映、`@onblur` で localStorage 保存
- 「詳細 ›」ボタンで `/stock/{code}` に遷移

### 個別銘柄画面（`Pages/StockDetail.razor`）

- ルート: `/stock/{Code}`
- 外部リンク（新しいタブで開く）
  - Yahoo Finance: `https://finance.yahoo.co.jp/quote/{Code}.T`
  - みんかぶ: `https://minkabu.jp/stock/{Code}`
  - 株探: `https://kabutan.jp/stock/?code={Code}`
- メモ・注目日・タグ（購入検討 / 保有中）の編集と localStorage 保存
- アラームバッジのリアルタイム表示
- 「株価情報」欄に「🔄 更新」ボタン → `StockApiService.FetchQuoteAsync` を呼び出し
  - 取得成功: 始値・高値・安値・前日終値・出来高・取得時刻を表示
  - 取得失敗: 警告メッセージと Yahoo Finance リンクを表示
- 財務指標（PER / PBR / ROE / 配当等）: **取得予定**プレースホルダー
- 株価チャート: SVG プレースホルダー（将来 API データに差し替え）
- 配当推移: 棒グラフ SVG（現状はモックデータ）
- 2 カラムグリッドレイアウト（モバイルは 1 カラム）

### レイアウト（`Layout/MainLayout.razor`）

- サイドバーを廃止し、固定ヘッダー + フルワイドコンテンツに変更
- ヘッダー: 「📈 KabuMemo」タイトル + 現在時刻

---

## 未実装（将来対応）

| 機能 | 理由 |
|---|---|
| 財務指標の自動取得 | PER / PBR / ROE / 配当推移など。適切な無料 API が未選定 |
| 株価チャート | 外部チャートライブラリまたは API が必要 |
| IR 情報一覧 | CLAUDE.md 記載の個別銘柄画面仕様 |

---

## 開発・デプロイ手順

### 開発サーバー起動

```bash
dotnet run
# または
dotnet watch
```

アクセス先: `http://localhost:5124`

### GitHub Pages 向けビルド

```bash
dotnet publish -c Release
# 出力先: bin/Release/net10.0/publish/wwwroot/
```

デプロイ時の注意:
- `wwwroot/index.html` の `<base href="/">` を `<base href="/KabuMemo/">` に変更
- `index.html` を `404.html` としてコピー（SPA ルーティング対策）
- CI は `.github/workflows/deploy.yml` で自動化

---

## 動作確認手順

1. `dotnet run` → `http://localhost:5124` にアクセス
2. 「＋ 銘柄を追加」→ 銘柄コード（例: `7203`）を入力し「名称検索」ボタンをクリック → 「トヨタ自動車」が自動入力されること
3. 「追加」→ 一覧に表示されること
4. メモ欄を入力 → フォーカスを外す → ブラウザをリロードして保存されていること
5. 注目日を本日以前に設定 → 黄色ハイライト + ⚠️ アラームバッジが表示されること
6. 「📊 株価更新」ボタン → 株価が取得され現在値欄に表示されること（CORS 制約で失敗する場合は警告表示）
7. 「詳細 ›」→ `/stock/{code}` に遷移し銘柄情報が表示されること
8. 詳細画面の「🔄 更新」ボタン → 始値・高値・安値・出来高が表示されること
9. 詳細画面でタグ・メモ・注目日を変更 → 一覧に戻って変更が反映されていること
10. 外部リンク（Yahoo Finance 等）が新しいタブで正しい URL を開くこと
11. 削除ボタン → 一覧から消え、リロード後も消えていること
