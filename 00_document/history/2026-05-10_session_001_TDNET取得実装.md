# セッション 002 — TDNET取得実装

- **日付:** 2026-05-10
- **作業内容:** プラン `snoopy-herding-hamster.md` に基づき、適時開示（TDNET）データ取得処理を `StockDetail.razor` および `app.css` へ実装

---

**User:**

C:\Users\takes\.claude\plans\snoopy-herding-hamster.md
を実行して

---

**Assistant:**

プランファイルを読み込みます。

*[ツール実行: Read — C:\Users\takes\.claude\plans\snoopy-herding-hamster.md]*

プランの内容（要旨）:
- WinFormsApp1 の適時開示取得処理を Blazor WASM へ移植する計画
- `Models/IrInfo.cs` と `Services/StockApiService.cs` の using 追加は完了済み
- 残り作業: `StockApiService.cs` への `FetchTdnetAsync` / `IrFetchResult` 追加、`StockDetail.razor` の IR セクション追加、`app.css` へのスタイル追加

プランを確認しました。現在のファイル状態を確認してから実装します。

*[ツール実行: Read — Services/StockApiService.cs]*

結果: `FetchTdnetAsync`・`ParseTdnet`・`StripHtml`・`IrFetchResult` はすでに実装済み。

*[ツール実行: Read — Pages/StockDetail.razor]*

結果: IR 関連コードは未実装。`} @* end else *@` の手前に追加が必要。

*[ツール実行: Read — wwwroot/css/app.css（offset:500〜675行）]*

`StockApiService.cs` はすでに完成しています。`StockDetail.razor` にIRセクションを追加し、CSSも更新します。

---

### 変更 1: Pages/StockDetail.razor — IR セクション HTML 追加

`<div class="dividend-card card-dividend">` の直前に挿入:

*[ツール実行: Edit — Pages/StockDetail.razor]*

```html
<div class="detail-card card-ir">
    <h3>適時開示（TDNET）</h3>
    @if (isIrLoading)
    {
        <div style="color:#aaa;font-size:13px;">読み込み中…</div>
    }
    else if (irError != null)
    {
        <div style="color:#e03535;font-size:12px;">@irError</div>
    }
    else if (irList.Count == 0)
    {
        <div style="color:#aaa;font-size:13px;">適時開示情報はありません。</div>
    }
    else
    {
        <table class="ir-table">
            <thead>
                <tr>
                    <th>日付</th>
                    <th>区分</th>
                    <th>タイトル</th>
                </tr>
            </thead>
            <tbody>
                @foreach (var ir in irList)
                {
                    <tr>
                        <td class="ir-date">@ir.Date</td>
                        <td class="ir-category">@ir.Category</td>
                        <td class="ir-title">
                            <a href="@ir.Url" target="_blank" rel="noopener noreferrer">@ir.Title ↗</a>
                        </td>
                    </tr>
                }
            </tbody>
        </table>
    }
</div>
```

---

### 変更 2: Pages/StockDetail.razor — @code フィールド追加

*[ツール実行: Edit — Pages/StockDetail.razor]*

```csharp
private List<IrInfo> irList = [];
private bool isIrLoading = false;
private string? irError = null;
```

---

### 変更 3: Pages/StockDetail.razor — OnInitializedAsync と LoadIrAsync 追加

*[ツール実行: Edit — Pages/StockDetail.razor]*

`OnInitializedAsync` に `_ = LoadIrAsync();` を追加し、以下のメソッドを新規追加:

```csharp
private async Task LoadIrAsync()
{
    if (stock == null) return;
    isIrLoading = true;
    StateHasChanged();

    var result = await ApiSvc.FetchTdnetAsync(stock.Code);
    irList = result.Success ? (result.Items ?? []) : [];
    irError = result.Success ? null : result.Error;

    isIrLoading = false;
    StateHasChanged();
}
```

---

### 変更 4: wwwroot/css/app.css — IR スタイル追加

*[ツール実行: Edit — wwwroot/css/app.css]*

`/* レスポンシブ */` ブロックの直前に挿入:

```css
/* 適時開示テーブル */
.card-ir { grid-column: 1 / -1; }

.ir-table { width: 100%; border-collapse: collapse; font-size: 12px; }
.ir-table th { text-align: left; padding: 6px 8px; border-bottom: 1px solid #333; color: #aab; font-weight: 600; }
.ir-table td { padding: 6px 8px; border-bottom: 1px solid #2a2a2a; vertical-align: top; }
.ir-date { white-space: nowrap; color: #aab; width: 90px; }
.ir-category { white-space: nowrap; color: #7eb8f7; width: 80px; }
.ir-title a { color: #90caf9; text-decoration: none; }
.ir-title a:hover { text-decoration: underline; }
```

---

### ビルド確認

*[ツール実行: Bash — dotnet build]*

```
ビルドに成功しました。
    0 個の警告
    0 エラー
経過時間 00:00:06.95
```

ビルド成功（警告・エラーなし）。実装した内容は以下の通りです：

**変更ファイル：**

1. `Pages/StockDetail.razor` — IRセクションのHTMLと`@code`にフィールド・`LoadIrAsync()`メソッドを追加
2. `wwwroot/css/app.css` — `.card-ir` / `.ir-table` などのスタイルを追加

**`StockApiService.cs`** は前回セッションで完成済みのためそのまま。

`dotnet run` で起動後、個別銘柄画面を開くと「適時開示（TDNET）」セクションが表示されます。CORS制約でirbank.netへのアクセスが失敗した場合はエラーメッセージが表示されます。
