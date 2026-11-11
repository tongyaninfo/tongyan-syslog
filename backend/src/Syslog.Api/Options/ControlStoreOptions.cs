namespace Syslog.Api.Options;

public sealed class ControlStoreOptions
{
    // 普通启动强制 ReadWrite；只有显式 CLI 或受限的首次配置提交允许创建控制库。
    public string ConnectionString { get; set; } = "Data Source=syslog-control.sqlite;Mode=ReadWrite";
    public int SessionLifetimeMinutes { get; set; } = 480;
    public int IdBlockSize { get; set; } = 4096;
}
