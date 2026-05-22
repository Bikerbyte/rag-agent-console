# Security Advisory RAG Agent 設計說明

這個專案的定位是 **Telegram-first CVE RAG Agent**。

它不是通用 Agent builder。它是把可替換的 AI provider、RAG retrieval、Telegram Bot API、CVE 資料管線和營運後台組成一個真的能跑的資安弱點助理。

## 核心方向

- 使用者透過 Telegram 以自然語言互動。
- 主要互動不依賴機器指令；Telegram callback 也送自然語言問題。
- 模型 provider 可切換 OpenAI API、Ollama local model，或 local deterministic fallback。
- 資安資料串接由本專案自行實作，避免只停留在提示詞層級。
- RAG 與回答流程保持可替換，目前可在 EfJson 與 pgvector 之間切換，後續也可以接 Qdrant 或其他 vector store。

## Agent Flow

```text
Telegram message
  -> TelegramUpdateProcessingService
  -> SecurityAdvisoryAgentService
  -> RAG retrieval
  -> OpenAI / Ollama / local answer
  -> Telegram reply
```

## 使用者可以直接問

```text
CVE-2024-3094 有什麼風險？
最近 Cisco 有哪些高風險 CVE？
今天有沒有 CISA KEV 新增項目？
Windows Azure Fortinet 近期有哪些 Critical 弱點？
哪些項目已經列入 CISA KEV？
```

## 自行實作的部分

- CISA KEV ingestion
- NVD ingestion
- `SecurityAdvisory` normalization
- chunk creation
- watchlist management
- notification dispatch
- Telegram update queue
- Agent reply orchestration
- Operations dashboard

## Service 分層

`Services/Agent` 放使用者訊息到回答的路徑，包含 RAG retrieval、AI provider client 與 Agent orchestration。

`Services/Advisories` 放弱點資料生命週期，包含 CISA / NVD source、同步、chunk 建立與通知派送。

`Services/Telegram` 放 Telegram 相關基礎設施，包含 bot client、webhook、polling、update queue 與 push。

`Services/Runtime` 放多節點執行時協調，例如 heartbeat 與 leadership lease。

`Services/Settings` 放後台可調整設定。`Services/Contracts` 只放依領域拆分的 interface，避免一個 contracts 檔案變成所有 service 的雜物箱。

## 可替換的部分

- Chat model：OpenAI / Ollama
- Embedding model：OpenAI / Ollama / local hash fallback
- Vector store：目前可使用 EfJson 或 PgVector，未來可替換成 Qdrant、Chroma 或 Milvus
- Runtime deployment：local dev、Docker、Linux VM、App Service

## RAG 責任切分

資料匯入層負責把外部資料來源轉成一致的 `SecurityAdvisory` 與 `SecurityAdvisoryChunk`。

Vector store 層由 `IAdvisoryVectorStore` 抽象出來。`ConfiguredAdvisoryVectorStore` 依設定選擇 `EfAdvisoryVectorStore` 或 `PgVectorAdvisoryVectorStore`。預設 `EfJson` 使用 EF Core / PostgreSQL 儲存 chunks 與 embedding JSON；`PgVector` 使用 PostgreSQL `vector` extension 做向量排序，並保留 JSON fallback。未來若要接 Qdrant、Chroma 或 Milvus，只要新增同一個 interface 的實作。

檢索層負責根據使用者問題、廠商名稱、CVE ID、severity、KEV 狀態建立 retrieval profile，交給 vector store 找候選 chunks，再排序出相關 context。

回答層只根據檢索回來的 context 回答，並在資訊不足時明確說明目前資料庫沒有足夠資料。這樣可以降低模型亂補細節的風險。

## MVP Acceptance

MVP 能力目標：

1. 本機不設定 API key 也能啟動。
2. 可以同步 CISA KEV / NVD 資料。
3. Operations page 可以直接測試自然語言 Agent。
4. Telegram 啟用後，使用者可用自然語言查 CVE 與查廠商弱點。
5. Provider 切成 OpenAI 或 Ollama 時，回答會由模型根據 RAG context 整理。
