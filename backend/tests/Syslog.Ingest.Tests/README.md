# Syslog.Ingest.Tests

接收与存储模块测试工程已建立。

- ClickHouseLogStoreTests 使用 HttpMessageHandler 替身验证参数化、来源权限、时间/页数预算、游标绑定、非法 UTF-8 原文、HTTP 尾部错误、超时、去重 token 和无自动重试；中文/纯符号查询明确拒绝。
- PipelineTests 使用内存暂存/存储/解析器替身验证禁用无 I/O、批次身份、sending 恢复停发、队列条数/字节限制和取消退出。
- LiveLogBufferTests 使用合成记录验证每设备 5000 默认条数、全局内存与元数据上界、立即缩容、来源权限、ASCII 全词过滤、原始字节保真、摘要/扫描预算、游标恢复与并发。LiveLogPipelineTests 复用内存暂存/存储替身验证忽略日志不发号不入库、暂存前可观察、写入重试不重复、旧暂存恢复不回填及观察失败不影响入库。
- Listener 测试使用模拟流或配置校验，不以真实端口、TLS 握手或设备作为测试前提。
- 独立协议切换用例另外在本机回环临时端口验证 TCP 连接保持和 TLS 端口释放，使用合成证书与报文；状态等待涵盖后台尚未发布首个状态的情况，不代表设备互操作验收。
- RelpSessionTests 使用内存双向流验证 open/syslog/close、协商拒绝、事务重复、半包/粘包、超长、取消及 ACK 超时；PipelineTests 验证文件暂存替身完成前不成功确认、失败返回拒绝。ListenerTests 覆盖未登记 RELP 内存样本与端点状态隔离。

实时缓存与流水线用例不调用 FileEventSpool.AcknowledgeAsync，不执行文件删除，不连接 ClickHouse 或网络端口。模拟测试不能证明真实 SQL 兼容性、索引命中、吞吐或掉电恢复；完整测试工程包含上述隔离回环监听用例。

从仓库根运行：

~~~sh
dotnet test backend/tests/Syslog.Ingest.Tests/Syslog.Ingest.Tests.csproj
~~~

[后端规则](../../AGENTS.md)
