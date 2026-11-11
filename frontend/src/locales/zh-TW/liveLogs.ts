import type source from '../zh-CN/liveLogs';

export default {
  'live.cacheId': '暫存識別碼',
  'menu.liveLogs': '即時看板',
  'live.title': '即時看板',
  'live.subtitle':
    '查看裝置最近接收的日誌，含依規則忽略的日誌。記憶體快取於服務重啟後清空。',
  'live.settings': '快取設定',
  'live.settingsHint':
    '儲存後立即生效。預設每裝置 5000 筆，記憶體總預算 512 MiB；達到上限即淘汰舊日誌。',
  'live.perDeviceLimit': '每裝置快取上限',
  'live.memoryLimit': '記憶體總預算',
  'live.limitValidation': '請輸入 100～50000 的整數。',
  'live.memoryValidation': '請輸入 16～2048 MiB。',
  'live.shrinkHint':
    '縮小快取會立即淘汰超出上限的舊日誌，已淘汰的快取無法復原。',
  'live.entries': '目前快取筆數',
  'live.devices': '裝置數',
  'live.memory': '目前快取記憶體',
  'live.budget': '總預算',
  'live.cacheHint': '各已登記來源獨立保留',
  'live.evicted': '累計淘汰',
  'live.rejected': '拒絕快取',
  'live.disposition': '處理方式',
  'live.disposition.all': '全部',
  'live.disposition.normal': '正常接收',
  'live.disposition.ignored': '已忽略 · 不入庫',
  'live.time.live': '不限時間 · 持續接收',
  'live.time.fixed': '固定時間範圍',
  'live.apply': '套用篩選',
  'live.dirty': '條件已修改，點擊套用後生效',
  'live.stream': '即時日誌',
  'live.connection.connecting': '連線中',
  'live.connection.connected': '即時連線正常',
  'live.connection.reconnecting': '重新連線中',
  'live.connection.disconnected': '連線已中斷',
  'live.connection.paused': '已暫停',
  'live.resume': '恢復即時',
  'live.pause': '暫停',
  'live.refresh': '重新讀取',
  'live.clear': '清空目前畫面',
  'live.evidenceHint':
    '即時記錄不代表已寫入資料庫；已忽略日誌不會入庫。清屏僅清空本瀏覽器畫面。向下捲動時自動暫停以保留閱讀位置。',
  'live.pausedHint':
    '畫面已凍結，此瀏覽器已停止訂閱；服務仍持續快取。恢復時嘗試補齊暫停期間的新日誌。',
  'live.gap':
    '快取已重設或部分記錄已淘汰，已重新讀取最近日誌。缺失記錄無法從即時快取復原。',
  'live.budgetLimited':
    '本批讀取達到數量或容量預算，僅顯示已取得的符合日誌；可繼續載入更早記錄或等待後續增量。',
  'live.spoolFull': '磁碟暫存已滿，接收日誌可能被丟棄。',
  'live.empty': '快取中暫無符合條件的日誌，正在等待新日誌。',
  'live.displayed': '畫面筆數',
  'live.heartbeat': '最近連線心跳',
  'live.queued': '待處理佇列',
  'live.dropped': '累計丟棄',
  'live.loadOlder': '載入更早 500 筆',
  'live.limitHint':
    '首次讀取最近 500 筆；畫面最多保留 5000 筆摘要，新記錄進入時移除最舊記錄。詳情按需從記憶體快取讀取。',
  'live.truncated': '摘要截斷',
  'live.normalHint':
    '正常接收：此記錄來自記憶體快取，不代表已寫入 ClickHouse。',
  'live.ignoredHint':
    '已依來源的嚴重等級規則忽略，僅保留即時記憶體樣本，不寫入資料庫。',
  'live.error.migration': '請先明確升級控制庫，再使用即時快取設定。',
  'live.error.cacheReset': '快取已重設，請重新讀取最近日誌。',
  'live.error.unavailable': '即時快取暫不可用，請檢查服務設定後重試。',
  'live.error.busy': '即時查詢忙碌，請稍後重試。',
  'live.error.evicted': '此日誌已從記憶體淘汰或快取已重啟，請重新讀取清單。',
  'live.error.conflict':
    '快取設定已被其他操作修改，請關閉後重新開啟設定。目前輸入已保留。',
} satisfies Record<keyof typeof source, string>;
