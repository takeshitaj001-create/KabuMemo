// KabuMemo API Proxy — Cloudflare Worker
// WinFormsApp1 と同じ Cookie+Crumb 認証方式で Yahoo Finance にアクセスする

const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36';

const CORS = {
  'Access-Control-Allow-Origin': '*',
  'Access-Control-Allow-Methods': 'GET, OPTIONS',
  'Access-Control-Allow-Headers': 'Content-Type',
};

// Worker インスタンス内でセッションをキャッシュ（crumb + cookies）
let cachedSession = null;

// ---- セッション管理 ----

function extractCookies(response) {
  try {
    // Cloudflare Workers は getAll('set-cookie') をサポート
    const all = response.headers.getAll('set-cookie');
    return all.map(c => c.split(';')[0].trim()).filter(Boolean).join('; ');
  } catch {
    const raw = response.headers.get('set-cookie') || '';
    return raw.split(/,(?=\s*[A-Za-z0-9_\-]+=)/)
      .map(c => c.split(';')[0].trim())
      .filter(Boolean)
      .join('; ');
  }
}

function mergeCookies(base, incoming) {
  const map = {};
  for (const pair of (base + '; ' + incoming).split('; ')) {
    const idx = pair.indexOf('=');
    if (idx < 0) continue;
    const k = pair.slice(0, idx).trim();
    const v = pair.slice(idx + 1);
    if (k) map[k] = v;
  }
  return Object.entries(map).map(([k, v]) => `${k}=${v}`).join('; ');
}

function extractField(html, name) {
  const esc = name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  let m = html.match(new RegExp(`name="${esc}"\\s+value="([^"]*)"`));
  if (!m) m = html.match(new RegExp(`value="([^"]*)"\\s+name="${esc}"`));
  return m ? m[1] : '';
}

async function refreshSession() {
  cachedSession = null;

  // Step 1: finance.yahoo.com にアクセスして Cookie を取得
  const homeResp = await fetch('https://finance.yahoo.com/', {
    headers: {
      'User-Agent': UA,
      'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8',
      'Accept-Language': 'ja-JP,ja;q=0.9,en-US;q=0.8,en;q=0.7',
    },
    redirect: 'follow',
  });

  let cookies = extractCookies(homeResp);
  const html = await homeResp.text();

  // Step 2: 同意ページが返った場合は同意フォームを送信
  if (html.includes('csrfToken') || html.includes('collectConsent')) {
    const sessionId = extractField(html, 'sessionId');
    if (sessionId) {
      const body = new URLSearchParams({
        agree: 'agree',
        consentid: sessionId,
        sessionId,
        originalDoneUrl: extractField(html, 'originalDoneUrl'),
        namespace: 'yahoo',
        csrfToken: extractField(html, 'csrfToken'),
      });
      const consentResp = await fetch('https://consent.yahoo.com/v2/collectConsent', {
        method: 'POST',
        body,
        headers: {
          'User-Agent': UA,
          'Cookie': cookies,
          'Content-Type': 'application/x-www-form-urlencoded',
        },
        redirect: 'follow',
      });
      cookies = mergeCookies(cookies, extractCookies(consentResp));
    }
  }

  // Step 3: crumb を query1 → query2 の順で取得
  for (const host of ['query1', 'query2']) {
    try {
      const crumbResp = await fetch(
        `https://${host}.finance.yahoo.com/v1/test/getcrumb`,
        { headers: { 'User-Agent': UA, 'Cookie': cookies, 'Accept': '*/*' } }
      );
      if (crumbResp.ok) {
        const crumb = (await crumbResp.text()).trim();
        if (crumb.length > 0 && !crumb.startsWith('<')) {
          cachedSession = { crumb, cookies };
          return;
        }
      }
    } catch {}
  }

  throw new Error('Yahoo Finance 認証に失敗しました（crumb 取得不可）');
}

// ---- エンドポイント処理 ----

// v7 result が空のとき v8 chart API（最大7日遡及）で最新終値を取得し v7 形式に変換
async function fetchChartFallback(code) {
  const { crumb, cookies } = cachedSession;
  const symbol = encodeURIComponent(code + '.T');

  // v8 chart API: query1 → query2（crumb あり・なし両方試す）
  for (const host of ['query1', 'query2']) {
    for (const withCrumb of [true, false]) {
      const crumbParam = withCrumb ? `&crumb=${encodeURIComponent(crumb)}` : '';
      const url = `https://${host}.finance.yahoo.com/v8/finance/chart/${symbol}?interval=1d&range=10d${crumbParam}`;
      const resp = await fetch(url,
        { headers: { 'User-Agent': UA, 'Cookie': cookies, 'Accept': 'application/json' } }
      );
      if (!resp.ok) continue;

      const data = await resp.json();
      const result = data?.chart?.result?.[0];
      if (!result) continue;

      const meta = result.meta ?? {};
      const q = result.indicators?.quote?.[0] ?? {};
      const closes = q.close ?? [];

      let idx = closes.length - 1;
      while (idx >= 0 && closes[idx] == null) idx--;
      if (idx < 0) continue;

      const close = closes[idx];
      const prevClose = idx > 0 ? (closes[idx - 1] ?? meta.previousClose ?? close)
                                : (meta.previousClose ?? close);
      const change = close - prevClose;
      const changePercent = prevClose ? (change / prevClose) * 100 : 0;

      return {
        quoteResponse: {
          result: [{
            regularMarketPrice: close,
            regularMarketChange: change,
            regularMarketChangePercent: changePercent,
            regularMarketOpen: q.open?.[idx] ?? close,
            regularMarketDayHigh: q.high?.[idx] ?? close,
            regularMarketDayLow: q.low?.[idx] ?? close,
            regularMarketPreviousClose: prevClose,
            regularMarketVolume: q.volume?.[idx] ?? 0,
          }],
          error: null,
        },
      };
    }
  }

  // v10 quoteSummary API にフォールバック
  for (const host of ['query1', 'query2']) {
    const crumbParam = `&crumb=${encodeURIComponent(crumb)}`;
    const resp = await fetch(
      `https://${host}.finance.yahoo.com/v10/finance/quoteSummary/${symbol}?modules=price${crumbParam}`,
      { headers: { 'User-Agent': UA, 'Cookie': cookies, 'Accept': 'application/json' } }
    );
    if (!resp.ok) continue;

    const data = await resp.json();
    const p = data?.quoteSummary?.result?.[0]?.price;
    if (!p) continue;

    const close = p.regularMarketPrice?.raw ?? 0;
    if (close <= 0) continue;

    return {
      quoteResponse: {
        result: [{
          regularMarketPrice: close,
          regularMarketChange: p.regularMarketChange?.raw ?? 0,
          regularMarketChangePercent: p.regularMarketChangePercent?.raw ?? 0,
          regularMarketOpen: p.regularMarketOpen?.raw ?? close,
          regularMarketDayHigh: p.regularMarketDayHigh?.raw ?? close,
          regularMarketDayLow: p.regularMarketDayLow?.raw ?? close,
          regularMarketPreviousClose: p.regularMarketPreviousClose?.raw ?? close,
          regularMarketVolume: p.regularMarketVolume?.raw ?? 0,
        }],
        error: null,
      },
    };
  }

  return null;
}

async function handleQuote(code) {
  if (!cachedSession) await refreshSession();

  const doFetch = () => fetch(
    `https://query1.finance.yahoo.com/v7/finance/quote` +
    `?symbols=${encodeURIComponent(code + '.T')}` +
    `&crumb=${encodeURIComponent(cachedSession.crumb)}` +
    `&lang=ja&region=JP`,
    {
      headers: {
        'User-Agent': UA,
        'Cookie': cachedSession.cookies,
        'Accept': 'application/json',
      },
    }
  );

  let resp = await doFetch();

  // 401 の場合はセッションを再取得して1回リトライ
  if (resp.status === 401) {
    await refreshSession();
    resp = await doFetch();
  }

  if (!resp.ok) throw new Error(`Yahoo Finance HTTP ${resp.status}`);

  let data = await resp.json();

  // result が空の場合は crumb 失効の可能性 → セッション再取得して1回リトライ
  if (!data?.quoteResponse?.result?.length) {
    await refreshSession();
    resp = await doFetch();
    if (!resp.ok) throw new Error(`Yahoo Finance HTTP ${resp.status}`);
    data = await resp.json();
  }

  // それでも空なら v8 chart API で最大7日遡及してフォールバック
  if (!data?.quoteResponse?.result?.length) {
    const fallback = await fetchChartFallback(code);
    if (fallback) data = fallback;
  }

  return new Response(JSON.stringify(data), {
    headers: { ...CORS, 'Content-Type': 'application/json' },
  });
}

async function handleTdnet(code) {
  const resp = await fetch(`https://irbank.net/${encodeURIComponent(code)}/tdnet/`, {
    headers: {
      'User-Agent': UA,
      'Accept': 'text/html',
      'Accept-Language': 'ja-JP,ja;q=0.9',
    },
  });
  if (!resp.ok) throw new Error(`irbank.net HTTP ${resp.status}`);
  const html = await resp.text();
  return new Response(html, {
    headers: { ...CORS, 'Content-Type': 'text/html; charset=utf-8' },
  });
}

async function handleDisclosures(code) {
  // /4591/ir を直接取得（Eコード変換不要）
  const resp = await fetch(`https://irbank.net/${encodeURIComponent(code)}/ir`, {
    headers: {
      'User-Agent': UA,
      'Accept': 'text/html',
      'Accept-Language': 'ja-JP,ja;q=0.9',
      'Referer': 'https://irbank.net/',
    },
    redirect: 'follow',
  });
  if (!resp.ok) throw new Error(`irbank.net HTTP ${resp.status}`);

  const html = await resp.text();
  const items = parseIrBankIr(html);

  return new Response(JSON.stringify(items), {
    headers: { ...CORS, 'Content-Type': 'application/json' },
  });
}

function parseIrBankIr(html) {
  const items = [];
  const seen = new Set();

  // title 属性に "（YYYY/MM/DD HH:MM提出）" が含まれる nxq リンクを全て抽出
  // sdl（直近）・dsp（過去）どちらの構造でも title 属性の形式は共通
  const linkRegex = /<a\s+class="nxq"\s+title="([^"]+)"\s+href="([^"]+)"/gi;
  let m;

  while ((m = linkRegex.exec(html)) !== null) {
    const titleAttr = m[1];
    const href = m[2];

    if (!href || href.startsWith('#') || href.startsWith('?')) continue;

    // title 属性から日付・時刻を抽出: 「（2026/6/3 12:30提出）」
    const dtMatch = titleAttr.match(/（(\d{4})\/(\d{1,2})\/(\d{1,2})\s+(\d{2}:\d{2})提出）/);
    if (!dtMatch) continue;

    const date = `${dtMatch[1]}-${dtMatch[2].padStart(2, '0')}-${dtMatch[3].padStart(2, '0')}`;
    const time = dtMatch[4];

    // タイトル整形: "コード 会社名 | " と末尾の提出日時を除去
    const cleanTitle = titleAttr
      .replace(/^\d+\s[^|]+\|\s*/, '')
      .replace(/（\d{4}\/\d{1,2}\/\d{1,2}\s+\d{2}:\d{2}提出）$/, '')
      .trim();

    if (!cleanTitle || cleanTitle.length < 3) continue;

    const url = href.startsWith('http') ? href : 'https://irbank.net' + href;

    const key = cleanTitle + date;
    if (seen.has(key)) continue;
    seen.add(key);

    items.push({ date, time, title: cleanTitle, url });
  }

  // 日付降順・直近1年に絞る
  items.sort((a, b) => b.date.localeCompare(a.date));
  const cutoff = new Date();
  cutoff.setFullYear(cutoff.getFullYear() - 1);
  const cutoffStr = cutoff.toISOString().slice(0, 10);
  return items.filter(x => x.date >= cutoffStr);
}

async function handleChart(code) {
  if (!cachedSession) await refreshSession();
  const { crumb, cookies } = cachedSession;
  const symbol = encodeURIComponent(code + '.T');

  const doFetch = () => fetch(
    `https://query1.finance.yahoo.com/v8/finance/chart/${symbol}` +
    `?interval=1d&range=1y&crumb=${encodeURIComponent(cachedSession.crumb)}`,
    { headers: { 'User-Agent': UA, 'Cookie': cachedSession.cookies, 'Accept': 'application/json' } }
  );

  let resp = await doFetch();
  if (resp.status === 401) {
    await refreshSession();
    resp = await doFetch();
  }
  if (!resp.ok) throw new Error(`chart API HTTP ${resp.status}`);

  const data = await resp.json();
  const result = data?.chart?.result?.[0];
  if (!result) throw new Error('チャートデータがありません');

  const timestamps = result.timestamp ?? [];
  const closes = result.indicators?.quote?.[0]?.close ?? [];

  const points = [];
  for (let i = 0; i < timestamps.length; i++) {
    if (closes[i] == null) continue;
    const date = new Date(timestamps[i] * 1000).toISOString().slice(0, 10);
    points.push({ date, close: Math.round(closes[i] * 10) / 10 });
  }

  return new Response(JSON.stringify(points), {
    headers: { ...CORS, 'Content-Type': 'application/json' },
  });
}

async function handleDividends(code) {
  if (!cachedSession) await refreshSession();
  const { crumb, cookies } = cachedSession;
  const symbol = encodeURIComponent(code + '.T');

  const resp = await fetch(
    `https://query1.finance.yahoo.com/v8/finance/chart/${symbol}` +
    `?events=div&range=5y&interval=1mo&crumb=${encodeURIComponent(crumb)}`,
    { headers: { 'User-Agent': UA, 'Cookie': cookies, 'Accept': 'application/json' } }
  );
  if (!resp.ok) throw new Error(`chart API HTTP ${resp.status}`);

  const data = await resp.json();
  const dividends = data?.chart?.result?.[0]?.events?.dividends ?? {};

  // 年度（4月始まり）ごとに集計。7-12月払い=中間配当、1-6月払い=期末配当（3月決算企業向け）
  // 例: 2024年9月払い→2024年度、2025年3月払い→2024年度
  const byYear = {};
  for (const entry of Object.values(dividends)) {
    const date = new Date(entry.date * 1000);
    const year = date.getFullYear();
    const month = date.getMonth() + 1; // 1-12
    const fiscalYear = month >= 4 ? year : year - 1; // 4月始まり年度
    if (!byYear[fiscalYear]) byYear[fiscalYear] = { amount: 0, interimAmount: 0, finalAmount: 0 };
    byYear[fiscalYear].amount += entry.amount;
    if (month >= 7) {
      byYear[fiscalYear].interimAmount += entry.amount;
    } else {
      byYear[fiscalYear].finalAmount += entry.amount;
    }
  }

  // 直近5年分（データがある年のみ）
  const years = Object.keys(byYear).map(Number).sort().slice(-5);
  const result = years.map(y => ({
    year: y,
    amount: Math.round(byYear[y].amount * 10) / 10,
    interimAmount: Math.round(byYear[y].interimAmount * 10) / 10,
    finalAmount: Math.round(byYear[y].finalAmount * 10) / 10,
  }));

  return new Response(JSON.stringify(result), {
    headers: { ...CORS, 'Content-Type': 'application/json' },
  });
}

async function translateChunkWithAI(chunk, env) {
  const result = await env.AI.run('@cf/meta/m2m100-1.2b', {
    text: chunk,
    source_lang: 'en',
    target_lang: 'ja',
  });
  return result?.translated_text ?? chunk;
}

async function translateChunkWithMyMemory(chunk) {
  const resp = await fetch(
    `https://api.mymemory.translated.net/get?q=${encodeURIComponent(chunk)}&langpair=en|ja`,
    { headers: { 'Accept': 'application/json' } }
  );
  if (!resp.ok) return chunk;
  const d = await resp.json();
  if (d?.responseStatus !== 200) return chunk;
  return d?.responseData?.translatedText ?? chunk;
}

function splitChunks(text, limit) {
  const sentences = text.match(/[^.!?]+[.!?]*/g) ?? [text];
  const chunks = [];
  let current = '';
  for (const s of sentences) {
    if ((current + s).length > limit && current) {
      chunks.push(current.trim());
      current = s;
    } else {
      current += s;
    }
  }
  if (current.trim()) chunks.push(current.trim());
  return chunks;
}

async function translateToJapanese(text, env) {
  if (!text) return '';

  const hasAI = !!env?.AI;

  // Workers AI を試みる（失敗したら MyMemory にフォールバック）
  if (hasAI) {
    try {
      const chunks = splitChunks(text, 500);
      const parts = await Promise.all(chunks.map(chunk => translateChunkWithAI(chunk, env)));
      return parts.join('');
    } catch (aiErr) {
      console.error('[translate] Workers AI failed, falling back to MyMemory:', aiErr?.message ?? aiErr);
    }
  }

  // MyMemory フォールバック（450 字制限・並列）
  try {
    const chunks = splitChunks(text, 450);
    const parts = await Promise.all(
      chunks.map(chunk => translateChunkWithMyMemory(chunk).catch(() => chunk))
    );
    return parts.join('');
  } catch {
    return text;
  }
}

async function handleSummary(code, env) {
  if (!cachedSession) await refreshSession();
  const { crumb, cookies } = cachedSession;
  const symbol = encodeURIComponent(code + '.T');

  const resp = await fetch(
    `https://query1.finance.yahoo.com/v10/finance/quoteSummary/${symbol}` +
    `?modules=assetProfile,calendarEvents&crumb=${encodeURIComponent(crumb)}`,
    { headers: { 'User-Agent': UA, 'Cookie': cookies, 'Accept': 'application/json' } }
  );
  if (!resp.ok) throw new Error(`quoteSummary HTTP ${resp.status}`);

  const data = await resp.json();
  const r0 = data?.quoteSummary?.result?.[0] ?? {};
  const profile = r0.assetProfile ?? {};
  const calendarEvents = r0.calendarEvents ?? {};

  const rawDescription = profile.longBusinessSummary ?? '';
  let description = '';
  let translationNote = null;

  if (rawDescription) {
    try {
      description = await translateToJapanese(rawDescription, env);
      if (description === rawDescription) {
        translationNote = 'untranslated';
      }
    } catch (e) {
      description = rawDescription;
      translationNote = String(e?.message ?? e);
    }
  }

  // 次回決算発表予定日（Unix タイムスタンプ → ISO 日付文字列）
  const earningsDates = calendarEvents?.earnings?.earningsDate ?? [];
  const nextEarningsDate = earningsDates.length > 0
    ? new Date(earningsDates[0].raw * 1000).toISOString().slice(0, 10)
    : null;

  const result = {
    description,
    employees: profile.fullTimeEmployees ?? null,
    city: profile.city ?? null,
    country: profile.country ?? null,
    earningsDate: nextEarningsDate,
    ...(translationNote ? { _translationNote: translationNote } : {}),
  };

  return new Response(JSON.stringify(result), {
    headers: { ...CORS, 'Content-Type': 'application/json' },
  });
}

async function handleName(query) {
  const decoded = decodeURIComponent(query);
  const isCode = /^\d+$/.test(decoded);

  if (isCode) {
    // 数値コード: .T を付けて検索し、返ってきた quotes を .T のみにフィルタ
    // Cloudflare IP から叩くと地域外銘柄（.SR 等）が優先されるため必須
    const resp = await fetch(
      `https://query1.finance.yahoo.com/v1/finance/search` +
      `?q=${encodeURIComponent(decoded + '.T')}&quotesCount=10&newsCount=0&lang=ja&region=JP`,
      { headers: { 'User-Agent': UA, 'Accept': 'application/json' } }
    );
    if (!resp.ok) throw new Error(`Yahoo US Search HTTP ${resp.status}`);
    const data = await resp.json();
    const quotes = (data.quotes ?? []).filter(q => q.symbol?.endsWith('.T'));
    // .T が一件も返らなかった場合はシンボルのみの候補を生成して返す
    if (quotes.length === 0) {
      return new Response(JSON.stringify({ quotes: [{ symbol: decoded + '.T', quoteType: 'EQUITY' }] }), {
        headers: { ...CORS, 'Content-Type': 'application/json' },
      });
    }
    return new Response(JSON.stringify({ quotes }), {
      headers: { ...CORS, 'Content-Type': 'application/json' },
    });
  }

  // 日本語テキスト: Yahoo Finance Japan の検索ページをスクレイプ（日本 IP では動作）
  // Cloudflare の海外エッジからは HTML 構造が変わり 0 件になる場合があるため
  // 結果が空のときは Yahoo Finance US にフォールバックする
  try {
    const jpResp = await fetch(
      `https://finance.yahoo.co.jp/search/?query=${encodeURIComponent(decoded)}&category=stock`,
      {
        headers: {
          'User-Agent': UA,
          'Accept': 'text/html',
          'Accept-Language': 'ja-JP,ja;q=0.9',
        },
      }
    );
    if (jpResp.ok) {
      const html = await jpResp.text();
      const quotes = [];
      const pattern = /"code":"(\d+)","marketName":"([^"]*)","name":"([^"]*)"/g;
      let m;
      while ((m = pattern.exec(html)) !== null) {
        const [, code, market, name] = m;
        if (!quotes.some(q => q.symbol === code + '.T')) {
          quotes.push({
            symbol: code + '.T',
            shortname: name,
            longname: name,
            exchDisp: market,
            quoteType: 'EQUITY',
          });
        }
      }
      if (quotes.length > 0) {
        return new Response(JSON.stringify({ quotes }), {
          headers: { ...CORS, 'Content-Type': 'application/json' },
        });
      }
    }
  } catch {}

  // フォールバック: Yahoo Finance US で検索し .T のみ返す
  // 日本語テキストは認識されにくいが、一部の英語対応銘柄名では有効
  const usResp = await fetch(
    `https://query1.finance.yahoo.com/v1/finance/search` +
    `?q=${encodeURIComponent(decoded)}&quotesCount=10&newsCount=0&lang=ja&region=JP`,
    { headers: { 'User-Agent': UA, 'Accept': 'application/json' } }
  );
  if (!usResp.ok) throw new Error(`Yahoo US Search HTTP ${usResp.status}`);
  const usData = await usResp.json();
  const usQuotes = (usData.quotes ?? []).filter(q => q.symbol?.endsWith('.T'));
  return new Response(JSON.stringify({ quotes: usQuotes }), {
    headers: { ...CORS, 'Content-Type': 'application/json' },
  });
}

// ---- メインハンドラ ----

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    if (request.method === 'OPTIONS') {
      return new Response(null, { status: 204, headers: CORS });
    }
    if (request.method !== 'GET') {
      return new Response('Method Not Allowed', { status: 405, headers: CORS });
    }

    const path = url.pathname;

    try {
      if (path.startsWith('/api/quote/')) {
        const code = path.slice('/api/quote/'.length);
        if (!code) return new Response('code required', { status: 400, headers: CORS });
        return await handleQuote(code);
      }

      if (path.startsWith('/api/chart/')) {
        const code = path.slice('/api/chart/'.length);
        if (!code) return new Response('code required', { status: 400, headers: CORS });
        return await handleChart(code);
      }

      if (path.startsWith('/api/dividends/')) {
        const code = path.slice('/api/dividends/'.length);
        if (!code) return new Response('code required', { status: 400, headers: CORS });
        return await handleDividends(code);
      }

      if (path.startsWith('/api/summary/')) {
        const code = path.slice('/api/summary/'.length);
        if (!code) return new Response('code required', { status: 400, headers: CORS });
        return await handleSummary(code, env);
      }

      if (path.startsWith('/api/tdnet/')) {
        const code = path.slice('/api/tdnet/'.length);
        if (!code) return new Response('code required', { status: 400, headers: CORS });
        return await handleTdnet(code);
      }

      if (path.startsWith('/api/disclosures/')) {
        const code = path.slice('/api/disclosures/'.length);
        if (!code) return new Response('code required', { status: 400, headers: CORS });
        return await handleDisclosures(code);
      }

      if (path.startsWith('/api/name/')) {
        const code = path.slice('/api/name/'.length);
        if (!code) return new Response('code required', { status: 400, headers: CORS });
        return await handleName(code);
      }

      return new Response('Not Found', { status: 404, headers: CORS });
    } catch (err) {
      return new Response(JSON.stringify({ error: err.message }), {
        status: 502,
        headers: { ...CORS, 'Content-Type': 'application/json' },
      });
    }
  },
};
