# CPBL Telegram Assistant

一個以 **ASP.NET Core** 開發的 Telegram Bot，主要用來整理與推送 **CPBL 中華職棒** 的比賽資訊。  
使用者可以直接在 Telegram 查詢今天賽程、即時比分、戰績排名、球隊近況，也可以追蹤喜歡的球隊，接收開賽、終場與新聞通知。

![System Architecture](docs/architecture/system-architecture.png)

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

---

## 主要指令

- `/today`：今天賽程，若有進行中的比賽也會一起顯示
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

另外也支援部分較自然的口語查詢，不一定只能輸入固定指令。

---

## 系統架構

整體上這個專案不是做成多服務分散式系統，而是維持在一個比較容易理解與維護的 **monolith**：

- **Telegram Users / Groups**  
  使用者透過 Telegram 與 bot 互動，查詢比賽資訊或接收主動通知。

- **Telegram Bot Platform**  
  作為 bot 對外的訊息入口，接收使用者指令並回傳系統整理後的內容。

- **ASP.NET Core Monolith**  
  核心應用本體，集中處理：
  - Telegram 指令解析
  - CPBL 資料同步
  - 通知排程
  - 文字整理與回覆格式化
  - Razor Pages 管理後台

- **PostgreSQL**  
  用來保存比賽資料、新聞、訂閱狀態與系統紀錄。

- **CPBL Official Source**  
  作為賽程、比分、排名與新聞的外部資料來源。

這樣的做法不是為了把技術堆得很滿，而是因為目前專案規模下，**單一應用 + 明確分層** 會比過早拆服務更實際。

---

## Demo

### 互動視窗
![Telegram demo](docs/demo/image.png)
![Telegram demo](docs/demo/image-1.png)

### 即時戰況更新
![Telegram demo](docs/demo/realtime_update.png)

---

## 技術組成

- **Backend**：ASP.NET Core
- **UI / Admin**：Razor Pages
- **Database**：PostgreSQL
- **Bot Platform**：Telegram Bot API
- **Deployment**：Docker
- **Data Source**：CPBL 官方資料

---

## 專案重點

### Telegram 作為互動入口
和一般網站不同，這個專案把 Telegram 當成主要操作介面。  
使用者不需要進網站查資料，只要在聊天室裡下指令，就可以快速拿到今天賽程、即時比分或球隊近況。

### 把原始資料整理成比較適合閱讀的內容
比起只把資料原封不動貼出來，這個 bot 會把 CPBL 原始資料整理成比較適合 Telegram 閱讀的格式，包含摘要、重點資訊與簡短 recap。

### 主動通知而不是只有被動查詢
除了查詢功能，也支援追蹤球隊與通知開關，讓系統可以主動推送開賽、終場與新聞等內容。

### 後台與 Bot 共用同一套系統
管理後台與 bot 核心邏輯放在同一個 ASP.NET Core 應用中，方便一起維護與除錯，也比較適合目前 side project 的規模。

---

## 環境設定

### 1. Clone 專案

```bash
git clone https://github.com/Bikerbyte/Baseball_Bot.git
cd Baseball_Bot
```

### 2. 設定資料庫連線

PostgreSQL：

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=cpbl_telegram_assistant;Username=postgres;Password=your-password"
```

### 3. 設定 Telegram Bot

先把 bot token 放進 user-secrets：

```powershell
dotnet user-secrets set "TelegramBot:Enabled" "true"
dotnet user-secrets set "TelegramBot:BotToken" "your-bot-token"
```

### 4. 執行專案

```bash
dotnet run
```

---

## 資料庫用途

保存以下資料，提供 bot 與後台管理使用：

- 比賽相關資料
- 新聞資料
- 使用者 / 群組訂閱設定
- 通知與操作紀錄
