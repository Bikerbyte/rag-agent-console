# RAG Agent 產品邏輯與 Knowledge Base 規劃

本文件記錄 Security Advisory Bot 後續要收斂的產品邏輯。目標不是把單一查詢案例用 rule 補洞，而是把整個專案整理成可驗證、可管理、可延伸的 CVE RAG Agent。

## 核心判斷

目前程式已經能跑通 Telegram、OpenAI / Ollama provider、PostgreSQL / pgvector、CISA KEV / NVD 同步與基本 RAG 回答，但現階段仍有一部分 query preprocessing 是由程式端 keyword / regex 先做。這比較像 MVP 的 search + RAG，不是最終希望的 Agentic RAG。

目前 CVE / 弱點查詢是第一個落地領域，但整體設計不應綁死在 CVE。更準確的定位是：這是一個可組裝的 RAG Agent runtime，CVE 弱點管理只是第一組 domain module。後續若要加入工作流程問答、內部 SOP 問答、客戶支援知識庫、合規文件查詢，應該能透過新增資料來源、planner schema、retriever 與 answer template 來擴充，而不是複製一套新的 bot。

設計方向：

```text
Core runtime
  -> Channel adapters: Telegram / Web Chat
  -> Model providers: OpenAI / Ollama / Local fallback
  -> Knowledge Base runtime: documents / chunks / embeddings / retrieval test
  -> Agent orchestration: planner / retrieval / answer / trace

Domain modules
  -> CVE Advisory module
  -> Workflow QA module
  -> Internal document QA module
  -> Future custom modules
```

CVE module 目前包含 CISA KEV、NVD、vendor / product / CVE 查詢、KEV 通知、watchlist。未來其他 module 不應改動 Telegram ingress、AI provider、vector store 或通用 Knowledge Base 管理流程。

正式產品邏輯應該是：

```text
Telegram / Web Chat
  -> Conversation Orchestrator
  -> LLM Query Planner
  -> Knowledge Base Retriever
  -> RAG Context Builder
  -> LLM Answer Generator
  -> Reply + Retrieval Trace
```

也就是使用者輸入應先交給 LLM 做 query understanding，再由 planner 產生 retrieval request，而不是讓程式用硬編碼 keyword 當主邏輯。

## Agent 查詢流程

使用者輸入範例：

```text
citrix netscaler 59.22 有已知弱點嗎
```

LLM Query Planner 應拆成結構化查詢意圖：

```json
{
  "intent": "vulnerability_lookup",
  "vendor": "Citrix",
  "product": "NetScaler",
  "version": "59.22",
  "riskFilter": "known_exploited",
  "retrievalQuery": "Citrix NetScaler known exploited vulnerabilities",
  "notes": [
    "version should be used as supporting context, not a hard retrieval filter unless advisory version ranges exist"
  ]
}
```

重要原則：

- `vendor`、`product` 可作為主要 retrieval 條件。
- `version` 應作為輔助判讀，不能預設當成硬過濾條件。
- `CVE ID` 可走精準查詢。
- `KEV`、`已知遭利用`、`高風險`、`Critical` 應轉成風險條件。
- 若資料庫沒有版本受影響範圍，回答時必須明確說「目前資料不足以確認此版本是否受影響」。

## Knowledge Base Retrieval Test

Knowledge Base 必須能先測 retrieval，而不是只能看 Agent 最終答案。這是 RAG 系統能不能被信任的關鍵。

頁面應提供一個 retrieval test 區塊：

```text
Query: citrix netscaler
Top K: 5
Mode: Hybrid / Vector / Keyword
```

回傳結果應至少包含：

| 欄位 | 說明 |
| --- | --- |
| Rank | 排序 |
| Score | 最終分數 |
| Vector score | 向量相似度 |
| Text score | 關鍵字 / structured field 分數 |
| CVE | CVE ID |
| Vendor | 廠商 |
| Product | 產品 |
| Severity | 嚴重性 |
| KEV | 是否 CISA KEV |
| Source | 資料來源 |
| Chunk preview | 實際送進 LLM 的 chunk 摘要 |

這個功能應該類似 Dify Knowledge 的 retrieval preview：先看知識庫到底找到了什麼，再決定 Agent 回答是否可信。

## Agent Retrieval Trace

Agent 回答時，Web console 應看得到 retrieval trace。Telegram 回覆可以保持簡潔，但 Web 後台需要可 audit。

Trace 建議格式：

```json
{
  "originalQuestion": "citrix netscaler 59.22 有已知弱點嗎",
  "planner": {
    "intent": "vulnerability_lookup",
    "vendor": "Citrix",
    "product": "NetScaler",
    "version": "59.22",
    "riskFilter": "known_exploited"
  },
  "retrieval": {
    "query": "Citrix NetScaler known exploited vulnerabilities",
    "topK": 5,
    "matches": [
      {
        "cve": "CVE-2025-5777",
        "score": 0.89,
        "source": "CISA KEV"
      }
    ]
  }
}
```

目的：

- 確認 LLM planner 是否理解問題。
- 確認 retrieval 是否抓到正確資料。
- 確認最終回答是否真的根據 retrieved context。
- 方便 debug Telegram 與 Web Chat 的回答差異。

## Knowledge Base 管理方向

Knowledge Base 頁面不應只是資料表。它應該是管理 RAG 資料的主要入口，參考 Dify 的 Knowledge 設計，但保持本專案的資安弱點領域焦點。

參考 Dify 畫面後，Knowledge Base 應具備兩種主要操作視角：

1. 建立知識庫 / 匯入資料
   - 以 stepper 呈現流程：選擇資料來源 -> 文字分段與清洗 -> 處理並完成。
   - Step 1 顯示資料來源卡片，例如「匯入已有文字」、「同步自 Web 網站」、「同步自外部工具」。
   - 對本專案而言，第一階段來源應是「官方弱點來源」、「上傳文字檔」、「手動貼上文字」、「同步 Web advisory URL」。
   - 上傳區應清楚顯示支援格式、檔案大小限制與批次限制。

2. 已建立知識庫 / 文件列表
   - 左側 rail 顯示目前 knowledge space 與功能入口。
   - 主要分頁包含「文件」、「管道」、「檢索測試」、「設定」。
   - 文件列表顯示檔名、分段模式、字元數、檢索次數、上傳時間、狀態、操作。
   - 右上角提供「新增檔案」、「元數據」等管理操作。

本專案不需要照抄 Dify 的品牌與 SaaS 包裝，但應吸收它的資訊架構：建立資料、管理文件、測試檢索、設定索引，四件事要分得很清楚。

應包含以下能力：

1. 資料來源總覽
   - CISA KEV
   - NVD CVE
   - 手動建立文件
   - 檔案上傳文件

2. 文件上傳
   - 支援 Markdown / TXT / PDF / DOCX / CSV 作為後續目標。
   - 初期可先支援 Markdown / TXT。
   - 上傳後進入 preprocessing / chunking。
   - 顯示 chunk preview 與 token / 字數估算。

3. 手動文字建立
   - 在 Web UI 直接貼上 advisory、內部 memo、廠商公告摘要。
   - 可設定 title、source、vendor、product、tags。
   - 儲存後進入 chunking / embedding。

4. Chunk 與 indexing 設定
   - Automatic：系統依預設 chunk rule 處理。
   - Custom：可調 chunk size、overlap、metadata。
   - 顯示預覽結果。

5. Retrieval Test
   - 查詢知識庫。
   - 顯示 top matches、score、chunk preview。
   - 可切換 keyword / vector / hybrid。

6. 文件管理
   - 啟用 / 停用資料。
   - 重新 embedding。
   - 刪除文件與 chunk。
   - 查看來源、同步時間、chunk 數、embedding 狀態。

7. Module-aware metadata
   - 每份文件應標記 module，例如 `CveAdvisory`、`WorkflowQa`、`InternalDocs`。
   - Retrieval Test 可選擇 module 範圍。
   - Agent Planner 可依 intent 選擇要查哪個 module。
   - 回答時應顯示來源 module，避免把 CVE 資料和工作流程文件混在一起。

## Knowledge Base 頁面設計方向

目前 Knowledge Base 頁面偏資料表與資訊堆疊，後續需要改成正式的知識庫工作台。

建議版面：

```text
Create Knowledge:
  Top stepper:
    1 Choose source
    2 Preprocess and clean
    3 Execute and finish

  Main:
    source cards
    upload / text input area
    next action

Knowledge Workspace:
  Left rail:
    Documents
    Channels
    Retrieval Test
    Settings

  Main:
    filters / search / sort
    document table
    status and actions

  Detail / Preview:
    metadata
    chunk preview
    retrieval matches
```

設計原則：

- 不做 landing page。
- 不放過多說明文字。
- 操作用正式產品語氣。
- 每個區塊功能清楚，不讓 Operations、Chat、Knowledge Base 混在一起。
- Retrieval Test 是 Knowledge Base 的核心能力之一，不是附屬 debug 工具。
- CVE 弱點資料是第一個 domain module，但 UI 命名要保留擴充性，例如使用 Knowledge / Documents / Modules，而不是所有地方都寫死 Advisory。

## 已落地的第一階段

- Knowledge Base 頁面已加入 Data Sources、文件匯入、文件列表與 Retrieval Test。
- 新增 `KnowledgeDocument` / `KnowledgeDocumentChunk`，讓官方 CVE advisory 之外，也能管理 user-managed documents。
- 文件 ingestion 已可由 Web UI 匯入手動文字與檔案，並建立 chunks / embeddings。
- 文件解析優先採用開源套件：Markdig、HtmlAgilityPack、CsvHelper、DocumentFormat.OpenXml。
- Chunking 使用 Microsoft Semantic Kernel TextChunker，避免自行維護分段演算法。
- Retrieval Test 已可用自然語言查詢目前 CVE Advisory module，並顯示 rank、score、CVE、vendor、product、risk 與 chunk preview。
- Agent 查詢已加入 `IAdvisoryQueryPlanner` 邊界；OpenAI / Ollama 啟用時可先用 LLM 產生 structured retrieval plan，Local fallback 則使用 deterministic planner。
- 版本號已從硬檢索條件中移除，改作為回答階段的不確定性說明，例如 `NetScaler 59.22` 會查 `Citrix NetScaler`，但回答會標明目前資料不足以確認該版本是否受影響。

## 待辦順序

1. 補強 Knowledge Base 頁面資訊架構
   - 拆成 Data Sources、Documents、Retrieval Test、Index Settings。
   - 下一步可將 Documents / Retrieval Test / Settings 拆成獨立分頁，降低單頁密度。

2. 將 user-managed documents 接入通用 retriever
   - 目前 Retrieval Test 已驗證 CVE Advisory module。
   - 下一步要讓 `KnowledgeDocumentChunks` 也進入 module-aware retriever。
   - 可依 module 選擇 `CveAdvisory`、`WorkflowQa`、`InternalDocs`。

3. 新增文件操作
   - 啟用 / 停用文件。
   - 刪除文件與 chunk。
   - 重新 embedding。
   - 顯示 parser、embedding 狀態與最後處理時間。

4. 補強 LLM Query Planner
   - 目前已建立 planner 邊界與 local fallback。
   - 下一步要將 planner output 記錄到 trace，並補強 schema validation 與錯誤可觀測性。
   - 後續可依 module 定義不同 planner schema。

5. 新增 Agent Retrieval Trace
   - Web Chat 顯示 planner 與 retrieval trace。
   - Telegram 維持簡潔回答。
   - Operations / Logs 可查詢歷史 trace。

6. 抽象 domain module 邊界
   - 定義 module metadata、planner schema、retrieval scope、answer template。
   - 先以 CVE Advisory module 實作。
   - 後續可新增 Workflow QA module，而不需要重寫 Telegram 或 Knowledge Base。

7. 評估 PDF parser
   - 目前已支援 Markdown、TXT、HTML、CSV、DOCX。
   - PDF 需要選擇穩定、可維護的 .NET parser 後再接入。

## 當前不做

- 不針對單一產品寫死特殊規則，例如只為 NetScaler 改 parser。
- 不先做大型 workflow builder；先保留 module 化邊界，等 CVE module 穩定後再擴充工作流程 QA。
- 不把 Knowledge Base 做成複雜 SPA。
- 不在 Telegram 回覆中塞完整 debug trace。

## 需要補充的設計參考

目前已有 Dify Knowledge 的方向作為參考，包含：

- 建立知識庫時的 stepper、資料來源卡片與上傳區。
- 知識庫文件列表的左側 rail、文件表格、檢索測試入口、設定入口。

若要更貼近 Dify 的 preprocessing、retrieval preview 操作體驗，建議後續再補一張或多張畫面：

- Knowledge list / dataset overview
- Create knowledge / upload file
- Text preprocessing and cleaning
- Retrieval testing / hit preview

收到參考畫面後，再進入 UI 重整與功能實作。
