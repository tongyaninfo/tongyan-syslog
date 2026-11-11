import type source from '../zh-CN/unregistered';

export default {
  'menu.unregistered': '未登記裝置',
  'unregistered.title': '未登記裝置',
  'unregistered.description':
    '查看尚未設定來源的傳送端 IP、近期日誌樣本，並一鍵登記來源。',
  'unregistered.boundary':
    '每個 IP 最多暫存最近 100 筆；僅保留於記憶體，重新啟動後清空。達到總容量或裝置數上限時淘汰最舊樣本。',
  'unregistered.defaults':
    '一鍵新增使用 IP 作為名稱，時區 Asia/Shanghai，保留 365 天，並允許接收。新增後可於「日誌來源」調整。',
  'unregistered.firstSeen': '首次接收',
  'unregistered.lastSeen': '最近接收',
  'unregistered.received': '累計暫存接收',
  'unregistered.samples': '目前樣本',
  'unregistered.viewLogs': '查看日誌',
  'unregistered.add': '一鍵新增',
  'unregistered.added': '已登記來源 {ip}',
  'unregistered.addedHint':
    '來源快照更新後開始正式接收，預設更新間隔 10 秒。暫存歷史不會回填至正式日誌。',
  'unregistered.addFailed': '新增失敗',
  'unregistered.loadFailed': '未登記裝置載入失敗',
  'unregistered.state': '暫存設定',
  'unregistered.enabled': '已啟用',
  'unregistered.disabled': '已關閉',
  'unregistered.devices': '目前裝置 / 上限',
  'unregistered.memory': '記憶體用量 / 上限',
  'unregistered.currentSamples': '目前樣本總數',
  'unregistered.evicted': '累計淘汰樣本',
  'unregistered.rejected': '累計拒絕',
  'unregistered.networks': '允許暫存網段',
  'unregistered.noNetworks': '未設定允許網段',
  'unregistered.counterHint':
    '這些計數反映暫存樣本，不代表已寫入資料庫。累計值從程序啟動開始；已登記但停用的來源不會進入此清單。啟用暫存不表示接收監聽已開啟。',
  'unregistered.search': '搜尋傳送端 IP',
  'unregistered.count': '目前篩選 {count} 個裝置',
  'unregistered.unavailable': '暫存資料無法使用',
  'unregistered.empty': '暫無未登記裝置',
  'unregistered.sampleExpired':
    '此樣本已淘汰或裝置已登記，請重新整理日誌清單。',
  'unregistered.byteCount': '原文大小',
  'unregistered.preview': '原文摘要',
  'unregistered.viewRaw': '查看原文',
  'unregistered.logsTitle': '{ip} · 暫存日誌',
  'unregistered.logsHint':
    '按接收時間倒序顯示最近 100 筆以內的樣本。僅供接入確認，未寫入正式日誌庫。',
  'unregistered.previewHint':
    '清單僅顯示有限長度的原文摘要；查看原文可檢查位元組證據、編碼異常及預覽截斷提示。',
  'unregistered.logsFailed': '暫存日誌載入失敗',
  'unregistered.noLogs': '暫無可用樣本，請重新整理確認。',
  'unregistered.helpStep1':
    '開啟「未登記裝置」，查看允許網段內尚未登記的傳送端 IP。已登記但停用的來源不會再次自動發現。',
  'unregistered.helpStep2':
    '每個 IP 最多保存最近 100 筆記憶體樣本，預設全域上限 64 MiB、1000 個 IP。達上限淘汰最舊樣本，重新啟動後清空。查看日誌後可按需查看單筆原文。',
  'unregistered.helpStep3':
    '點選「一鍵新增」以 IP 名稱、Asia/Shanghai 時區、365 天保留及啟用接收建立來源。來源快照更新後正式接收，預設間隔 10 秒；暫存歷史不回填。之後可於「日誌來源」修改設定。',
} satisfies Record<keyof typeof source, string>;
