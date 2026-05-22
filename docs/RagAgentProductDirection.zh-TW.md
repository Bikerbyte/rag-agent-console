# RAG Agent 產品邏輯與 Knowledge Base 規劃

本文件記錄 Security Advisory Bot 後續要收斂的產品邏輯。目標不是把單一查詢案例用 rule 補洞，而是把整個專案整理成可驗證、可管理、可延伸的 CVE RAG Agent。

## 核心判斷

目前程式已經能跑通 Telegram、OpenAI / Ollama provider、PostgreSQL / pgvector、CISA KEV / NVD 同步與基本 RAG 回答，但現階段仍有一部分 query preprocessing 是由程式端 keyword / regex 先做。這比較像 MVP 的 search + RAG，不是最終希望的 Agentic RAG。

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

## Knowledge Base 頁面設計方向

目前 Knowledge Base 頁面偏資料表與資訊堆疊，後續需要改成正式的知識庫工作台。

建議版面：

```text
Left rail:
  Data Sources
  Documents
  Retrieval Test
  Index Settings

Main:
  selected workflow / table / editor

Right preview:
  chunk preview
  retrieval matches
  document metadata
```

設計原則：

- 不做 landing page。
- 不放過多說明文字。
- 操作用正式產品語氣。
- 每個區塊功能清楚，不讓 Operations、Chat、Knowledge Base 混在一起。
- Retrieval Test 是 Knowledge Base 的核心能力之一，不是附屬 debug 工具。

## 待辦順序

1. 重整 Knowledge Base 頁面資訊架構
   - 拆成 Data Sources、Documents、Retrieval Test、Index Settings。
   - 降低目前頁面的雜訊與擁擠感。

2. 新增 Retrieval Test
   - 直接輸入 query。
   - 顯示 top matches、score、metadata、chunk preview。
   - 讓使用者先驗證知識庫檢索是否正確。

3. 新增文件 / 手動文字管理模型
   - 建立 document entity。
   - 文件與 advisory chunk 關聯。
   - 區分 official advisory 與 user-managed document。

4. 新增手動文字建立
   - Web UI 可直接貼文字。
   - 可填 title、source、vendor、product、tags。
   - 儲存後產生 chunks / embeddings。

5. 新增檔案上傳
   - 初期支援 `.md`、`.txt`。
   - 後續再加入 PDF / DOCX parser。
   - 上傳後進入 preprocessing preview。

6. 新增 LLM Query Planner
   - 使用 OpenAI / Ollama 將使用者問題轉成 structured retrieval request。
   - planner output 需要 schema validation。
   - local fallback 才使用簡化 keyword parser。

7. 新增 Agent Retrieval Trace
   - Web Chat 顯示 planner 與 retrieval trace。
   - Telegram 維持簡潔回答。
   - Operations / Logs 可查詢歷史 trace。

## 當前不做

- 不針對單一產品寫死特殊規則，例如只為 NetScaler 改 parser。
- 不先做大型 workflow builder。
- 不把 Knowledge Base 做成複雜 SPA。
- 不在 Telegram 回覆中塞完整 debug trace。

## 需要補充的設計參考

目前已有 Dify Knowledge 的方向作為參考。若要更貼近 Dify 的上傳、preprocessing、retrieval preview 操作體驗，建議補一張或多張畫面：

- Knowledge list / dataset overview
- Create knowledge / upload file
- Text preprocessing and cleaning
- Retrieval testing / hit preview

收到參考畫面後，再進入 UI 重整與功能實作。
