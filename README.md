# CPBL Telegram Assistant

以 **ASP.NET Core** 開發的 Telegram Bot，用來整理與推送 **CPBL 中華職棒** 的比賽資訊。  
使用者可以直接在 Telegram 查詢今日賽程、即時比分、戰績排名、球隊近況，也可以追蹤喜歡的球隊，接收開賽、終場與新聞通知。

---

## 系統架構

<img width="2012" height="902" alt="CPBL drawio" src="https://github.com/user-attachments/assets/f8ad67ca-00db-49cb-88a0-4968c9bf3217" />

採取 **same codebase, multi-node deployment by runtime roles** 的設計：

- 流量分配機制：具備多節點承接 session 的能力。
- `Webhook ingress` 可多台同時啟用，用於承接 Telegram update
- `Telegram update queue worker` 採多節點協作模式，從共享 inbox queue claim batch 後處理
- `scheduled jobs`（例如官方資料同步、通知排程）則採 **lease-based single-active execution**
- PostgreSQL 除了保存業務資料，也作為多節點協調的共享狀態層，保存：
  - Telegram update inbox
  - runtime node heartbeat
  - runtime leadership lease

- HA：每台節點都具備執行 `scheduled jobs` 資格，但同一時間只會有一台節點持有對應 lease 並真正執行；若目前持有 lease 的節點離線或未持續續約，其他節點會自動接手。

---

## 目前功能

- 查今天賽程、明天賽程
- 查即時比分與對戰狀態
- 查昨天賽果、今日完賽結果
- 查目前排名
- 查指定球隊的近況與下一場比賽
- 追蹤喜歡的球隊
- 接收開賽、終場、新聞等通知
- 查看最新中職新聞
- 以簡短文字整理當日比賽重點
- 透過 Razor Pages 後台管理訂閱、log、runtime node 與 leadership lease 狀態

---

## 主要指令

- `/today`：今天賽程
- `/tomorrow`：明天賽程
- `/yesterday`：昨天已完賽結果
- `/result`：今天已完賽結果
- `/standings`：目前排名
- `/follow 兄弟`：追蹤指定球隊
- `/following`：查看目前追蹤隊伍
- `/team 兄弟`：查看球隊近況摘要
- `/next 兄弟`：查看這支球隊下一場比賽
- `/notify`：查看提醒狀態
- `/notify game on`：開啟比賽提醒
- `/recap`：查看今日賽事重點整理
- `/news`：查看最新新聞

---
## Demo

- 追蹤球隊之及時戰況
<img alt="demo1" src="docs\demo\realtime_update.png" />

- 指令 & 按鈕快捷
<img alt="demo1" src="docs\demo\news_demo.png" />

- 管理後臺 & 節點查看
<img alt="demo1" src="docs\demo\dashboard.png" />

---

## 環境設定

### 1. Clone 專案

```bash
git clone https://github.com/Bikerbyte/Baseball_Bot.git
cd Baseball_Bot
```

### 2. 設定資料庫連線

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=cpbl_telegram_assistant;Username=postgres;Password=your-password"
```

### 3. 設定 Telegram Bot

```powershell
dotnet user-secrets set "TelegramBot:Enabled" "true"
dotnet user-secrets set "TelegramBot:BotToken" "your-bot-token"
```

### 4. 套用 migration

```bash
dotnet ef database update
```

### 5. 部署

實際部署方式參考 [docs/Ubuntu_Deployment.zh-TW.md](docs/Ubuntu_Deployment.zh-TW.md)。
