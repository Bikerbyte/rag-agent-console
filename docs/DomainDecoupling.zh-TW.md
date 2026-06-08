# Domain Decoupling Notes

這個專案的核心目標是可換領域的 RAG Agent Console，而不是只能回答資安問題的 bot。

## 現況

- 通用層：AI provider、embedding、pgvector/EF JSON vector store、BM25、hybrid retrieval、web chat、Telegram runtime、Settings。
- Sample domain：CISA KEV / NVD connector、CVE Advisory module、advisory push。
- 通用文件：Knowledge Base 支援 Markdown、TXT、HTML、CSV、DOCX 與 manual text import。

## 為什麼保留 CVE Advisory module

CVE/CISA/NVD 是目前的內建 sample connector。它提供公開、可重現、容易展示 retrieval quality 的資料來源。保留它可以讓專案一啟動就有資料，但產品敘事不應該被它綁住。

## 面試展示建議

1. 先展示 Dashboard，說明這是 domain-adaptable RAG console。
2. 到 Knowledge Base 匯入 `docs/demo-corpus/onboarding-policy.zh-TW.md`，module 選 `InternalDocs`。
3. 在 Retrieval Test 查詢 `remote work approval` 或 `新人前三十天目標`。
4. 再切到 CVE Advisory sample，展示同一套 retrieval pipeline 可以查不同 domain。
5. 說明下一步可把 sample connector interface 抽象化，讓 HR、產品文件、客服 FAQ、法遵政策都能成為 connector。

## 後續重構方向

- 將 `SecurityAdvisory*` service/entity 逐步改名為 `SampleRecord*` 或 `DomainRecord*`。
- 將 CISA/NVD sync 從 core services 移到 connector folder。
- 讓 module schema 可設定，例如 `RiskSignal`、`Owner`、`ProductArea`、`EffectiveDate`。
- 為 demo corpus 加入 automated seed/import command。
