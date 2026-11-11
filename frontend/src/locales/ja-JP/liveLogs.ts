import type source from '../zh-CN/liveLogs';

export default {
  'live.cacheId': '一時キャッシュ ID',
  'menu.liveLogs': 'リアルタイムログ',
  'live.title': 'リアルタイムログ',
  'live.subtitle':
    '無視されたログを含む最新の受信ログを確認します。メモリキャッシュは再起動時に消去されます。',
  'live.settings': 'キャッシュ設定',
  'live.settingsHint':
    '保存後すぐに反映します。既定はデバイスごとに 5000 件、全体で 512 MiB。上限に達すると古いログを破棄します。',
  'live.perDeviceLimit': 'デバイスごとの上限',
  'live.memoryLimit': 'メモリ総量上限',
  'live.limitValidation': '100～50000 の整数を入力してください。',
  'live.memoryValidation': '16～2048 MiB を入力してください。',
  'live.shrinkHint':
    '上限を減らすと古いキャッシュが直ちに破棄されます。復元はできません。',
  'live.entries': 'キャッシュ件数',
  'live.devices': 'デバイス数',
  'live.memory': 'キャッシュ使用量',
  'live.budget': '総量上限',
  'live.cacheHint': '登録済みソースごとに保持',
  'live.evicted': '累計破棄',
  'live.rejected': 'キャッシュ拒否',
  'live.disposition': '処理区分',
  'live.disposition.all': 'すべて',
  'live.disposition.normal': '受信',
  'live.disposition.ignored': '無視・DB 保存なし',
  'live.time.live': 'キャッシュ全期間・受信継続',
  'live.time.fixed': '固定期間',
  'live.apply': '条件を適用',
  'live.dirty': '条件が変更されています。適用してください。',
  'live.stream': 'ライブログ',
  'live.connection.connecting': '接続中',
  'live.connection.connected': '接続中・正常',
  'live.connection.reconnecting': '再接続中',
  'live.connection.disconnected': '切断',
  'live.connection.paused': '一時停止',
  'live.resume': '受信を再開',
  'live.pause': '一時停止',
  'live.refresh': '再読み込み',
  'live.clear': '画面をクリア',
  'live.evidenceHint':
    '表示は DB 書き込みを保証しません。無視したログは DB に保存しません。クリアはこの画面のみです。下にスクロールすると読み取り位置を保持するため一時停止します。',
  'live.pausedHint':
    '画面の購読は停止しています。サーバーはキャッシュを継続し、再開時に差分を取得します。',
  'live.gap':
    'キャッシュのリセットまたは破棄が発生したため最新ログを再取得しました。失われた記録は復元できません。',
  'live.budgetLimited':
    '今回の読み取りは件数または容量の上限に達しました。以前の記録を読み込むか、次の更新を待ってください。',
  'live.spoolFull':
    'ディスクの一時領域が満杯です。受信ログが失われる可能性があります。',
  'live.empty': '一致するキャッシュがありません。新しいログを待機しています。',
  'live.displayed': '表示件数',
  'live.heartbeat': '最終ハートビート',
  'live.queued': '処理待ち',
  'live.dropped': '累計ドロップ',
  'live.loadOlder': '以前の 500 件を読み込む',
  'live.limitHint':
    '最初に最新 500 件を表示します。画面は最大 5000 件の概要を保持し、新着時に最古の記録を除去します。詳細は必要時にメモリから取得します。',
  'live.truncated': '概要省略',
  'live.normalHint':
    '受信済み：メモリ上の記録であり、ClickHouse への保存確認ではありません。',
  'live.ignoredHint':
    'ソースの重大度ルールにより無視されました。メモリのみで保持し、DB には書き込みません。',
  'live.error.migration':
    '設定を使用する前に管理 DB を明示的にアップグレードしてください。',
  'live.error.cacheReset':
    'キャッシュがリセットされました。再読み込みしてください。',
  'live.error.unavailable':
    'キャッシュを利用できません。サービス設定を確認して再試行してください。',
  'live.error.busy': '検索が混雑しています。後で再試行してください。',
  'live.error.evicted':
    '記録が破棄されたかキャッシュが再起動しました。一覧を更新してください。',
  'live.error.conflict':
    '設定が他で変更されました。閉じて再度開いてください。入力は保持されています。',
} satisfies Record<keyof typeof source, string>;
