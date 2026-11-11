export default {
  'menu.unregistered': '未登记设备',
  'unregistered.title': '未登记设备',
  'unregistered.description':
    '查看尚未配置来源的发送端 IP、近期日志样本，并一键登记来源。',
  'unregistered.boundary':
    '每个 IP 最多暂存最近 100 条；仅在内存中保留，重启后清空。达到总容量或设备数上限时淘汰最旧样本。',
  'unregistered.defaults':
    '一键添加使用 IP 作为名称，时区 Asia/Shanghai，保留 365 天，并允许接收。添加后可在“日志来源”调整。',
  'unregistered.firstSeen': '首次接收',
  'unregistered.lastSeen': '最近接收',
  'unregistered.received': '累计暂存接收',
  'unregistered.samples': '当前样本',
  'unregistered.viewLogs': '查看日志',
  'unregistered.add': '一键添加',
  'unregistered.added': '已登记来源 {ip}',
  'unregistered.addedHint':
    '来源快照刷新后开始正式接收，默认刷新间隔 10 秒。暂存历史不会回填到正式日志。',
  'unregistered.addFailed': '添加失败',
  'unregistered.loadFailed': '未登记设备加载失败',
  'unregistered.state': '暂存配置',
  'unregistered.enabled': '已启用',
  'unregistered.disabled': '已关闭',
  'unregistered.devices': '当前设备 / 上限',
  'unregistered.memory': '内存用量 / 上限',
  'unregistered.currentSamples': '当前样本总数',
  'unregistered.evicted': '累计淘汰样本',
  'unregistered.rejected': '累计拒绝',
  'unregistered.networks': '允许暂存网段',
  'unregistered.noNetworks': '未配置允许网段',
  'unregistered.counterHint':
    '这些计数反映临时样本，不代表已入库。累计值从进程启动开始；已登记但停用的来源不会进入此列表。暂存启用不表示监听器已开启。',
  'unregistered.search': '搜索发送端 IP',
  'unregistered.count': '当前筛选 {count} 个设备',
  'unregistered.unavailable': '暂存数据不可用',
  'unregistered.empty': '暂无未登记设备',
  'unregistered.sampleExpired': '该样本已淘汰或设备已登记，请刷新日志列表。',
  'unregistered.byteCount': '原文大小',
  'unregistered.preview': '原文摘要',
  'unregistered.viewRaw': '查看原文',
  'unregistered.logsTitle': '{ip} · 暂存日志',
  'unregistered.logsHint':
    '按接收时间倒序显示最近 100 条以内的样本。仅用于接入确认，未进入正式日志库。',
  'unregistered.previewHint':
    '列表仅显示有限长度的原文摘要；查看原文可检查字节证据、编码异常及预览截断提示。',
  'unregistered.logsFailed': '暂存日志加载失败',
  'unregistered.noLogs': '暂无可用样本，请刷新确认。',
  'unregistered.helpStep1':
    '打开“未登记设备”，查看允许网段内尚未登记的发送端 IP。已登记但停用的来源不会再次自动发现。',
  'unregistered.helpStep2':
    '每个 IP 最多保存最近 100 条内存样本，默认全局上限 64 MiB、1000 个 IP。达到上限淘汰最旧样本，重启后清空。查看日志后可按需查看单条原文。',
  'unregistered.helpStep3':
    '点击“一键添加”按 IP 名称、Asia/Shanghai 时区、365 天保留及启用接收创建来源。来源快照刷新后正式接收，默认间隔 10 秒；暂存历史不回填。后续可在“日志来源”修改配置。',
};
