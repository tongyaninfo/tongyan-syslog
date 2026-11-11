import type source from '../zh-CN/unregistered';

export default {
  'menu.unregistered': '未登録デバイス',
  'unregistered.title': '未登録デバイス',
  'unregistered.description':
    '未登録の送信元 IP と最近のログサンプルを確認し、ワンクリックでソースを登録します。',
  'unregistered.boundary':
    '各 IP の最新 100 件までをメモリに一時保存します。再起動で消去され、メモリやデバイス数の上限では最古のサンプルから除去されます。',
  'unregistered.defaults':
    'クイック追加では IP を名前に使用し、タイムゾーン Asia/Shanghai、保存期間 365 日、受信有効で登録します。後からログソースで変更できます。',
  'unregistered.firstSeen': '初回受信',
  'unregistered.lastSeen': '最終受信',
  'unregistered.received': 'サンプル受信累計',
  'unregistered.samples': '現在のサンプル',
  'unregistered.viewLogs': 'ログを見る',
  'unregistered.add': 'クイック追加',
  'unregistered.added': 'ソース {ip} を登録しました',
  'unregistered.addedHint':
    'ソース情報の更新後に通常受信が始まります（既定間隔 10 秒）。一時保存の履歴は正式ログに移されません。',
  'unregistered.addFailed': 'ソースを追加できませんでした',
  'unregistered.loadFailed': '未登録デバイスを読み込めませんでした',
  'unregistered.state': '一時保存設定',
  'unregistered.enabled': '有効',
  'unregistered.disabled': '無効',
  'unregistered.devices': 'デバイス数 / 上限',
  'unregistered.memory': 'メモリ / 上限',
  'unregistered.currentSamples': '現在のサンプル総数',
  'unregistered.evicted': '除去サンプル累計',
  'unregistered.rejected': '拒否累計',
  'unregistered.networks': '許可ネットワーク',
  'unregistered.noNetworks': '許可ネットワーク未設定',
  'unregistered.counterHint':
    'この値は一時サンプルの状態であり、データベースへの書き込み数ではありません。累計はプロセス起動から計測します。登録済みで無効なソースは表示されません。一時保存の有効化はリスナーの有効化ではありません。',
  'unregistered.search': '送信元 IP を検索',
  'unregistered.count': '該当デバイス {count} 台',
  'unregistered.unavailable': 'サンプルデータを利用できません',
  'unregistered.empty': '未登録デバイスはありません',
  'unregistered.sampleExpired':
    'サンプルが除去されたかデバイスが登録されました。ログ一覧を更新してください。',
  'unregistered.byteCount': '原文サイズ',
  'unregistered.preview': '原文プレビュー',
  'unregistered.viewRaw': '原文を見る',
  'unregistered.logsTitle': '{ip} · 一時ログ',
  'unregistered.logsHint':
    '最新の最大 100 件を受信日時の降順で表示します。接続確認用で、正式ログデータベースには保存されていません。',
  'unregistered.previewHint':
    '一覧は長さ制限付きの原文プレビューです。原文を開くとバイト列、文字コード、切り詰めの注意を確認できます。',
  'unregistered.logsFailed': '一時ログを読み込めませんでした',
  'unregistered.noLogs': 'サンプルがありません。更新して確認してください。',
  'unregistered.helpStep1':
    '未登録デバイスを開くと、許可ネットワーク内の未登録送信元 IP を確認できます。登録済みで無効なソースは再検出されません。',
  'unregistered.helpStep2':
    '各 IP で最新 100 件までをメモリに保持します。既定の全体上限は 64 MiB と 1000 IP です。上限で最古のサンプルを除去し、再起動ですべて消去します。ログから個別の原文を開けます。',
  'unregistered.helpStep3':
    'クイック追加は IP 名、Asia/Shanghai タイムゾーン、365 日保存、受信有効で登録します。ソース情報の更新後（既定 10 秒）に通常受信が始まります。サンプル履歴は移されません。設定は後からログソースで変更できます。',
} satisfies Record<keyof typeof source, string>;
