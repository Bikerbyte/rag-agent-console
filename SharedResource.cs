namespace RagAgentConsole;

/// <summary>
/// 多語系共用資源的標記型別。對應 Resources/SharedResource.{culture}.resx。
/// 介面文字以「繁體中文原文」作為 resource key：預設文化 (zh-Hant) 直接顯示 key，
/// 切換到英文時則由 SharedResource.en.resx 提供對應翻譯。
/// </summary>
public sealed class SharedResource
{
}
