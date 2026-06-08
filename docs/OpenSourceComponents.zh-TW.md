# 開源元件使用清單

本文件列出 RAG Agent Console 目前使用到的開源元件、用途，以及主要程式碼位置。專案原則是：能用成熟開源元件處理的解析、chunking、資料庫、telemetry 與本機模型能力，不自行重刻。

## Runtime / Web

| 元件 | 用途 | 主要位置 |
| --- | --- | --- |
| ASP.NET Core Razor Pages | Web Console、Settings、Chat、Knowledge Base 頁面 | `Program.cs`、`Pages/` |
| Entity Framework Core | PostgreSQL / InMemory 資料存取、migration、DbContext | `Data/ApplicationDbContext.cs`、`Migrations/` |
| Npgsql EntityFrameworkCore PostgreSQL | PostgreSQL provider | `Program.cs`、`SecurityAdvisoryBot.csproj` |
| Serilog | 結構化 logging | `Program.cs`、`appsettings*.json` |
| OpenTelemetry | traces / metrics 匯出 | `Program.cs`、`Models/Options.cs` |

## RAG / Knowledge Base

| 元件 | 用途 | 主要位置 |
| --- | --- | --- |
| PostgreSQL pgvector | 向量檢索 extension，支援 `PgVector` vector store | `Services/Agent/PgVectorAdvisoryVectorStore.cs`、`docker-compose.yml` |
| BM25 | sparse retrieval scoring，支援 hybrid retrieval 與 evaluation | `Services/Agent/Retrieval/Bm25Index.cs`、`Services/Agent/AdvisoryTextScorer.cs` |
| Microsoft Semantic Kernel TextChunker | 文件 chunking，避免自行維護分段演算法 | `Services/Knowledge/KnowledgeTextChunkingService.cs` |
| Markdig | Markdown 文字解析 | `Services/Knowledge/KnowledgeDocumentTextExtractor.cs` |
| HtmlAgilityPack | HTML 文字抽取 | `Services/Knowledge/KnowledgeDocumentTextExtractor.cs` |
| CsvHelper | CSV 轉可檢索文字 | `Services/Knowledge/KnowledgeDocumentTextExtractor.cs` |
| DocumentFormat.OpenXml | DOCX 文字抽取 | `Services/Knowledge/KnowledgeDocumentTextExtractor.cs` |

## AI Provider / Local Model

| 元件 | 用途 | 主要位置 |
| --- | --- | --- |
| OpenAI API | 雲端 chat completion 與 embedding provider | `Services/Agent/AiProviderClients.cs`、`Models/Options.cs`、`Pages/Settings/Index.cshtml.cs` |
| Ollama | 本機 LLM / embedding provider，可用開源模型 | `Services/Agent/AiProviderClients.cs`、`Models/Options.cs`、`Pages/Settings/Index.cshtml.cs` |
| Local deterministic fallback | 無 API key 時的本機 hash embedding 與 deterministic planner | `Services/Agent/AiProviderClients.cs`、`Services/Agent/AdvisoryQueryPlanner.cs` |

## External Data / Channel

| 元件或 API | 用途 | 主要位置 |
| --- | --- | --- |
| Telegram Bot API | Telegram-first 使用者入口、polling、webhook、推播 | `Services/Telegram/`、`Program.cs`、`Models/TelegramModels.cs` |
| CISA KEV Catalog | 已知遭利用弱點資料來源 | `Services/Advisories/AdvisorySources.cs` |
| NVD CVE API | CVE 資料來源 | `Services/Advisories/AdvisorySources.cs` |

## Testing / Development

| 元件 | 用途 | 主要位置 |
| --- | --- | --- |
| xUnit | 單元測試 | `SecurityAdvisoryBot.Tests/` |
| EF Core InMemory | 測試與無連線字串時的本機資料庫 fallback | `SecurityAdvisoryBot.Tests/`、`Program.cs` |
| Docker Compose | PostgreSQL / pgvector 本機環境 | `docker-compose.yml` |

## 目前保留的自製部分

以下是本專案刻意自製的 glue code，原因是它們屬於產品邏輯，而不是通用基礎設施：

- `Services/Agent/AdvisoryQueryPlanner.cs`：把自然語言問題轉成 module-aware retrieval plan；OpenAI / Ollama 啟用時會優先讓 LLM 規劃，local fallback 僅作離線保底。
- `Services/Agent/SecurityAdvisoryRagServices.cs`：負責 planner、retriever、context builder、answer generator 與 trace 組裝。
- `Services/Agent/*VectorStore.cs`：把 PostgreSQL / pgvector 與 EF JSON fallback 包成一致的 retriever 介面。
- `Services/Knowledge/KnowledgeDocumentIngestionService.cs`：把上傳文件、手動文字、chunk、embedding、module metadata 串成一條 ingestion pipeline。
- `Services/Telegram/`：Telegram update queue、polling、webhook、reply dispatch 的產品整合。
