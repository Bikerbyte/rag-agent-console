# Security Advisory RAG Rework

這份文件說明目前 lightweight security advisory intelligence bot 的設計方向。

目標不是做 Dify clone，也不是做大型 workflow platform，而是保留現有 ASP.NET Core / EF Core / Telegram / worker / admin console 的工程骨架，換成更適合履歷展示的資安情報場景。

## 核心定位

系統負責：

- 同步 CVE / security advisory 來源
- 正規化成 `SecurityAdvisory`
- 產生簡短摘要與建議處置方向
- 建立 lightweight RAG chunks
- 讓 Telegram 使用者用 `/ask` 追問
- 依照 chat 訂閱關鍵字推送高風險或已遭利用弱點
- 在 Razor Pages 後台檢查資料、同步紀錄與推播狀態

## 第一版範圍

目前第一刀已接上：

- CISA KEV JSON feed
- NVD CVE API
- `SecurityAdvisories` / `SecurityAdvisoryChunks`
- local hash embedding service
- RAG search / answer service
- Telegram commands:
  - `/latest`
  - `/critical`
  - `/kev`
  - `/explain CVE-2024-3094`
  - `/ask 最近 Fortinet 有哪些風險？`
  - `/subscribe fortinet azure windows`
  - `/watchlist`
  - `/sync`
- `Advisories` admin page
- Telegram subscription fields for advisory push

## RAG 設計取捨

這版刻意不先導入 LangChain、Dify、Qdrant 或 Weaviate。

原因：

- 專案要維持可維護，不要為了展示 RAG 把架構變重
- 目前資料量用 PostgreSQL + EF Core + local embedding 已經足夠 demo
- 先把 ingestion、chunk、retrieve、answer 的產品流程做清楚
- 未來真的需要更高品質搜尋時，再把 embedding store 換成 pgvector 或 OpenSearch

目前的 RAG 是：

```text
CISA / NVD
  -> SecurityAdvisory
  -> SecurityAdvisoryChunk
  -> local hash embedding
  -> cosine retrieval
  -> grounded Telegram answer with source URLs
```

## Linux 或 Windows

開發可以繼續在 Windows 上做，因為 .NET、EF Core、Razor Pages 和 Telegram webhook 都能跨平台跑。

正式部署建議偏 Linux：

- Docker / Nginx / PostgreSQL 比較自然
- 未來如果加 pgvector，也比較接近真實 production stack
- background worker、health check、log collection 在 Linux VM 或 container 上比較好展示
- 履歷與面試時比較容易說明雲端部署拓撲

建議維持：

```text
Windows: local development
Linux: demo / production deployment
PostgreSQL: shared app data, worker lease, advisory store
```

## 後續建議

下一步優先順序：

1. 加入 pgvector migration 或保留 local embedding 作為 dev fallback
2. 加入 OpenAI-compatible summary service，沒有 API key 時沿用 local summary
3. 擴充 security command service 的單元測試
4. 補新版 Linux 部署文件與 demo 截圖
