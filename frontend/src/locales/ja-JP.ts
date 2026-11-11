import httpsCertificate from './ja-JP/httpsCertificate';
import listeners from './ja-JP/listeners';
import liveLogs from './ja-JP/liveLogs';
import mcp from './ja-JP/mcp';
import notifications from './ja-JP/notifications';
import unregistered from './ja-JP/unregistered';
import users from './ja-JP/users';
import type source from './zh-CN';

export default {
  ...liveLogs,
  ...mcp,
  ...notifications,
  ...httpsCertificate,
  ...listeners,
  ...unregistered,
  ...users,
  'app.title': '通研 Syslog',
  'app.brand': '通研技術',
  'app.description': 'ログの一元管理プラットフォーム',
  'app.logout': 'ログアウト',
  'app.logoutFailed':
    'ログアウトできませんでした。セッションは有効です。もう一度お試しください。',
  'app.theme': '表示モード',
  'app.light': 'ライト',
  'app.dark': 'ダーク',
  'app.auto': '自動',
  'app.account': '管理者',
  'app.preview': '合成データのプレビュー · 画面確認用',
  'app.sessionLoading': 'セッションを確認しています…',
  'app.retry': '再読み込み',
  'app.notFound': 'ページが見つかりません',
  'app.back': '概要に戻る',
  'app.timezone': '北京時間 · UTC+8',
  'app.appearance': 'テーマ設定',
  'app.appearanceStyle': '全体の表示スタイル',
  'app.autoHint': '自動モードは OS のライト／ダーク設定に従います。',
  'app.language': '表示言語',
  'app.userMenu': 'アカウントメニュー',
  'app.changePassword': 'パスワード変更',
  'app.currentPassword': '現在のパスワード',
  'app.newPassword': '新しいパスワード',
  'app.confirmPassword': '新しいパスワードの確認',
  'app.passwordRequired': 'パスワードを入力してください。',
  'app.passwordLength': '新しいパスワードは 8～1024 文字で入力してください。',
  'app.passwordMismatch': '新しいパスワードが一致しません。',
  'app.passwordChanged': 'パスワードを変更しました。再度ログインしてください。',
  'app.passwordHint': '変更すると、すべてのセッションからログアウトします。',
  'app.passwordPreviewHint':
    'プレビューでは入力と再ログインの手順を確認できます。サンプルのパスワードは変わりません。',
  'app.passwordPreviewChanged':
    '手順の確認が完了しました。プレビュー用のパスワードで再度ログインしてください。',
  'app.cancel': 'キャンセル',
  'app.save': '保存',
  'login.welcome': 'おかえりなさい',
  'login.subtitle': 'ログインして、デバイスのログと受信状況を確認できます。',
  'login.hero': 'すべてのログを、追跡できる形に。',
  'login.description':
    'デバイスとアプリのログを集約し、一つの画面で検索・調査・管理できます。',
  'login.collect': 'ログの集中受信',
  'login.collectDescription': 'スイッチ、ESXi、アプリの Syslog を集約します',
  'login.search': 'すばやく検索',
  'login.searchDescription':
    '時刻、送信元、重大度、キーワードでイベントを特定できます',
  'login.evidence': '原文の確認',
  'login.evidenceDescription': '構造化フィールドと元のメッセージを照合できます',
  'login.username': 'ユーザー名',
  'login.password': 'パスワード',
  'login.usernameRequired': 'ユーザー名を入力してください',
  'login.passwordRequired': 'パスワードを入力してください',
  'login.submit': '管理コンソールにログイン',
  'login.sessionHint':
    '安全なセッションを使います。コンソールはログインパスワードをブラウザーに保存しません。',
  'login.initialAccount':
    '管理者アカウントはシステム管理者が事前に作成します。',
  'parse.parsed': '解析済み',
  'parse.failed': '解析失敗',
  'parse.unknown': '形式不明',
  'severity.0': '緊急',
  'severity.1': '警報',
  'severity.2': '重大',
  'severity.3': 'エラー',
  'severity.4': '警告',
  'severity.5': '通知',
  'severity.6': '情報',
  'severity.7': 'デバッグ',
  'error.generic': 'サービスを利用できません。時間をおいて再試行してください。',
  'error.auth': 'ユーザー名またはパスワードが正しくありません。',
  'error.session':
    'セッションの有効期限が切れました。再度ログインしてください。',
  'error.input': '入力内容を確認して再試行してください。',
  'error.query': '検索期間、ページの件数、絞り込み条件を確認してください。',
  'error.token':
    'キーワードには ASCII の英数字を使用してください。大文字と小文字を区別し、単語全体で一致させます。',
  'error.permission': 'このアカウントには操作権限がありません。',
  'error.notFound':
    'レコードが存在しないか、期限切れまたはアクセスできません。',
  'error.conflict':
    'この送信元 IP は登録済みです。設定の重複を確認してください。',
  'error.csrf':
    'リクエスト元を検証できませんでした。コンソールの元の URL から再度ログインしてください。',
  'error.https':
    'ログインには安全な接続が必要です。HTTPS のコンソール URL を使用してください。',
  'error.rate': 'リクエストが多すぎます。時間をおいて再試行してください。',
  'error.timeout':
    'リクエストがタイムアウトしました。再試行するか検索範囲を狭めてください。',
  'error.reconcile': '保存記録の照合が必要です。管理者に連絡してください。',
  'error.network':
    'サービスに接続できません。ネットワークを確認して再試行してください。',
  'error.currentPassword': '現在のパスワードが正しくありません。',
  'error.passwordInvalid': '新しいパスワードが要件を満たしていません。',
  'error.passwordUnchanged': '現在とは異なるパスワードを指定してください。',
  'error.tooManyRequests':
    'リクエストが多すぎます。時間をおいて再試行してください。',
  'logs.title': 'ログ検索',
  'logs.subtitle':
    '時刻と送信元でログを検索し、解析結果と元のメッセージを確認します。',
  'logs.range': '受信時刻',
  'logs.timezone': '時刻は Asia/Shanghai で表示',
  'logs.rangeHint':
    '1 回の検索は最大 30 日です。保存期間内の過去の日時も選べます。',
  'logs.lastHour': '過去 1 時間',
  'logs.lastDay': '過去 24 時間',
  'logs.lastWeek': '過去 7 日間',
  'logs.lastMonth': '過去 30 日間',
  'logs.source': 'ログ送信元',
  'logs.allSources': 'すべての登録済み送信元',
  'logs.severity': '重大度',
  'logs.allSeverities': 'すべての重大度',
  'logs.hostname': 'ホスト名',
  'logs.hostnamePlaceholder': 'メッセージ内のホスト名と完全一致',
  'logs.appName': 'アプリ名',
  'logs.appNamePlaceholder': 'アプリ名と完全一致',
  'logs.keywords': '本文のキーワード',
  'logs.keywordsPlaceholder': '例: LINK DOWN',
  'logs.keywordHint':
    'ASCII の英数字を含む単語全体で、大文字と小文字を区別して検索します。すべての単語の一致が必要です。非 ASCII や部分一致には対応していません。',
  'logs.pageSize': '1 ページの件数',
  'logs.pageSizeValue': '{count} 件',
  'logs.search': 'ログを検索',
  'logs.clearAction': 'ログを消去',
  'logs.clearTitle': '保存済みログをすべて消去',
  'logs.clearSourceTitle': 'この機器のログを消去',
  'logs.clearSourceScope':
    '{name}（{address}）の保存済みログ履歴を消去します。',
  'logs.clearSourceSucceeded': '{name} のログ履歴を消去しました。',
  'logs.clearSubmit': '消去を確定',
  'logs.clearScope':
    'ClickHouse に保存されたすべての Syslog レコードを完全に消去します。現在の検索条件に含まれないレコードも対象です。',
  'logs.clearPendingLogs':
    '消去後も受信と保存は続きます。保存待ちのログが後から表示される場合があります。',
  'logs.clearConfirmation': '確認のために CLEAR と入力',
  'logs.clearSucceeded': 'ログを消去しました。',
  'logs.clearInProgress':
    '別の消去処理が進行中です。完了を待ってから検索結果を更新してください。',
  'logs.clearSizeLimit':
    'ログテーブルが消去可能なサイズの上限を超えています。管理者に ClickHouse の設定確認を依頼してください。',
  'logs.clearOutcomeUnknown':
    '消去結果を確認できませんでした。再実行する前に検索して結果を確認してください。',
  'logs.clearStorageAccess':
    'ログストレージが消去を拒否しました。管理者にストレージアカウントの権限確認を依頼してください。',
  'logs.clearFailed':
    '消去を完了できませんでした。サービスの状態を確認してください。',
  'logs.reset': 'リセット',
  'logs.refresh': '再検索',
  'logs.results': '検索結果',
  'logs.resultsHint': '一致したログは受信時刻の新しい順に表示されます。',
  'logs.page': '{page} ページ',
  'logs.rows': 'このページは {count} 件',
  'logs.previous': '前へ',
  'logs.next': '次へ',
  'logs.noMore': '検索結果の最後です',
  'logs.empty': '一致するログはありません',
  'logs.emptyHint':
    '時刻、送信元、またはキーワードを変更して再試行してください。',
  'logs.pending': '絞り込み条件を変更しました',
  'logs.pendingHint': '「ログを検索」を押して現在の条件を適用します。',
  'logs.loading': 'ログを検索しています',
  'logs.queryFailed': 'ログ検索を完了できませんでした',
  'logs.pageFailed': '{page} ページを読み込めませんでした',
  'logs.restartFromFirstPage': '最初のページから再検索',
  'logs.error.storageConnection':
    'ログストレージに接続できません。ClickHouse の稼働状態と接続設定を確認してから再試行してください。',
  'logs.error.storageAccess':
    'ログストレージへのアクセスが拒否されました。管理者にストレージのアカウントと読み取り権限の確認を依頼してください。',
  'logs.error.storageSchema':
    '必要なログデータベースまたはテーブルがありません。管理者にストレージの初期化状態の確認を依頼してください。',
  'logs.error.storageSchemaIncompatible':
    '既存の ClickHouse ログテーブルに互換性がありません。管理者にテーブル定義の確認を依頼してください。',
  'logs.error.storageTimeout':
    'ログストレージの要求がタイムアウトしました。時間範囲を短くして再試行できます。',
  'logs.error.storageUnavailable':
    'ログストレージが要求を完了できませんでした。稼働状態を確認してから再試行してください。',
  'logs.error.invalidResponse':
    'ログサービスの応答を読み取れませんでした。再試行し、問題が続く場合は管理者に連絡してください。',
  'logs.error.queryUnavailable':
    'ログ検索は一時的に利用できません。現在の条件で再試行し、問題が続く場合は管理者に連絡してください。',
  'logs.sourcesFailed': '送信元の一覧を取得できません',
  'logs.sourcesRetry': '送信元の再読み込み',
  'logs.noSources': 'ログ送信元は未登録です',
  'logs.noSourcesHint':
    '送信元の管理でデバイスのアドレスを登録してから、ログ送信を設定してください。',
  'logs.receivedAt': '受信時刻',
  'logs.eventTime': 'デバイス時刻',
  'logs.message': 'ログ本文',
  'logs.parseStatus': '解析状態',
  'logs.parserVersion': 'パーサーのバージョン',
  'logs.action': '操作',
  'logs.detail': '詳細を表示',
  'logs.detailTitle': 'ログの詳細',
  'logs.detailLoading': 'ログの詳細を読み込んでいます',
  'logs.detailFailed': 'ログを読み込めません',
  'logs.close': '閉じる',
  'logs.retry': '再試行',
  'logs.eventId': 'イベント ID',
  'logs.sourceId': '送信元 ID',
  'logs.peerAddress': '接続元',
  'logs.transport': '転送プロトコル',
  'logs.expiresAt': '保存期限',
  'logs.timeConfidence': '時刻の解釈',
  'logs.originalTimestamp': '元のタイムスタンプ',
  'logs.procId': 'プロセス ID',
  'logs.msgId': 'メッセージ ID',
  'logs.facility': 'ファシリティ',
  'logs.failureCode': '解析診断',
  'logs.structuredData': '構造化データ',
  'logs.original': '元のメッセージ',
  'logs.originalHint':
    '受信したバイト列が元データです。上のフィールドと本文は解析結果です。',
  'logs.originalText': 'テキストプレビュー',
  'logs.originalHex': '16 進数プレビュー',
  'logs.originalSize': '原文 {count} バイト',
  'logs.originalEmpty': '元のメッセージは空です',
  'logs.invalidBase64':
    '元のメッセージのエンコードが無効なためプレビューできません。',
  'logs.rawTooLarge':
    '元のメッセージが上限を超えたためプレビューを停止しました。',
  'logs.invalidUtf8':
    '原文に UTF-8 ではないバイトが含まれます。テキストでは代替文字を表示します。16 進数プレビューで確認してください。',
  'logs.textTruncated': 'テキストプレビューは先頭 {count} 文字までです。',
  'logs.hexTruncated': '16 進数プレビューは先頭 {count} バイトまでです。',
  'logs.messageTruncated': '表示内容は先頭 {count} 文字までです。',
  'logs.notProvided': '未指定',
  'logs.notApplicable': '—',
  'logs.time.explicit': 'メッセージに時刻とタイムゾーンが明記されています',
  'logs.time.inferred_year_timezone': '送信元のタイムゾーンから年を推定',
  'logs.time.inferred_ambiguous': '夏時間の曖昧さを受信時刻から解決',
  'logs.time.inferred_distant': '推定時刻が受信時刻から大きく離れています',
  'logs.time.unknown': '有効なデバイス時刻がありません',
  'logs.validation.range':
    '有効な開始時刻と終了時刻を選び、終了時刻を開始時刻より後にしてください。',
  'logs.validation.maxRange': '検索期間は 30 日以内にしてください。',
  'logs.validation.keywordsLength': 'キーワードは 512 文字以内にしてください。',
  'logs.validation.keywords':
    'キーワードには ASCII の英数字を含めてください。記号だけでは検索できません。',
  'logs.validation.fieldLength':
    'ホスト名とアプリ名は 255 文字以内にしてください。',
  'logs.validation.pageSize':
    '1 ページの件数は 1～500 の整数で指定してください。',
  'logs.validation.severity': '有効な重大度を選んでください。',
  'menu.overview': '稼働状況',
  'menu.logs': 'ログ検索',
  'menu.sources': 'ログ送信元',
  'menu.retention': '保存設定',
  'overview.title': '稼働状況',
  'overview.description':
    'ログの受信、解析、一時保存、書き込みの状況を確認します。',
  'overview.refresh': '更新',
  'overview.autoRefresh': 'リアルタイム更新',
  'overview.lastReceive': '最終ログ受信: {time}',
  'overview.refreshedAt': 'ダッシュボード更新: {time}',
  'overview.rate.title': 'ログ処理レート',
  'overview.rate.sourceFilter': 'ログの送信元で絞り込む',
  'overview.rate.allSources': 'すべて',
  'overview.rate.received': '受信レート',
  'overview.rate.written': '書き込みレート',
  'overview.rate.unit': '件/秒',
  'overview.rate.window': '直近 {seconds} 秒',
  'overview.rate.waiting': 'リアルタイム計測データを待機中',
  'overview.rate.latest': '最新レート',
  'overview.rate.sampleTime': '計測時刻',
  'overview.rate.noSample': '計測データなし',
  'overview.rate.ignored': '除外レート',
  'overview.rate.chartLabel':
    'ログの受信・書き込み・除外レートのリアルタイム折れ線グラフ',
  'overview.live.connecting': '接続中',
  'overview.live.connected': 'リアルタイム接続は正常です',
  'overview.live.reconnecting': '再接続中',
  'overview.live.disconnected': 'リアルタイム接続が切断されました',
  'overview.live.paused': 'リアルタイム更新を一時停止中',
  'overview.live.disconnectedHint':
    '接続が切断され、表示中のデータは更新されていません。再接続してください。',
  'overview.live.waitingHint': 'サーバーからの最新データを待機しています。',
  'overview.live.pausedHint':
    '一時停止前のデータを表示しています。リアルタイム更新をオンにするか、「更新」を選択して最新の状態を取得してください。',
  'overview.received': '受信数',
  'overview.receivedHint': 'アプリで観測したメッセージ · {bytes}',
  'overview.written': '書き込み済み',
  'overview.writtenHint': 'ログストレージへの書き込みを確認済み',
  'overview.dropped': '破棄数',
  'overview.droppedHint': 'レート制限、上限超過、キューあふれなどによる破棄',
  'overview.sources': '登録済み送信元',
  'overview.sourcesHint':
    '有効 {enabled} 件 · 設定上の状態であり、接続状況とは異なります',
  'overview.pipeline': '受信と解析',
  'overview.listener': '受信リスナー',
  'overview.ingest': '処理タスク',
  'overview.parsed': '解析成功',
  'overview.parseFailed': '解析失敗',
  'overview.parseUnknown': '不明な形式',
  'overview.lastWrite': '最終書き込み成功時刻（上海時間）',
  'overview.noWrite': '書き込み成功なし',
  'overview.buffer': '一時保存と書き込み',
  'overview.queueBytes': '受信キュー使用量',
  'overview.spoolBytes': '現在のローカルスプール使用量',
  'overview.pendingBatches': '現在の書き込み待ちバッチ',
  'overview.databaseSize': '現在のログデータベースサイズ',
  'overview.writeFailures': '書き込み失敗回数',
  'overview.bufferHint':
    '書き込みの滞留を判断するには、待機中のバッチ数とローカルスプール使用量が増え続けているか確認してください。',
  'overview.counterHint':
    '累計値はプロセスの起動から集計し、再起動でリセットされます。OS が受信する前の損失は含まれません。',
  'overview.statusError': '稼働状況を取得できません',
  'overview.sourceError': '送信元の情報を取得できません',
  'overview.emptyTitle': 'ログ送信元は未登録です',
  'overview.emptyDescription':
    'デバイスまたはアプリの送信元アドレスを追加し、接続設定に従ってログを転送します。',
  'overview.manageSources': '送信元の管理',
  'overview.runtimeIssue': '処理タスクを確認してください',
  'overview.retryTitle': 'ログの書き込みが一時的に滞っています',
  'overview.retryHint':
    'バッチはローカルスプールに残り、書き込みは自動で再試行されます。',
  'overview.listenerIssue': '受信接続を確認してください',
  'overview.state.running': '稼働中',
  'overview.state.listening': '受信待機中',
  'overview.state.starting': '起動中',
  'overview.state.disabled': '無効',
  'overview.state.stopped': '停止',
  'overview.state.failed': '異常',
  'overview.state.retrying': '自動再試行中',
  'overview.state.writePaused': '書き込み一時停止',
  'overview.state.unknown': '—',
  'overview.issue.writePaused':
    '一部のバッチの書き込み結果が未確認のため、書き込みを一時停止しています。',
  'overview.issue.storageConnection':
    'ClickHouse に接続できません。サービスの稼働状態と接続設定を確認してください。',
  'overview.issue.storageAccess':
    'ClickHouse が書き込みを拒否しました。アカウントと書き込み権限を確認してください。',
  'overview.issue.storageTimeout':
    'ClickHouse への書き込みまたはストレージの確認がタイムアウトしました。',
  'overview.issue.storageUnavailable':
    'ログストレージは書き込み要求を完了できませんでした。',
  'overview.issue.clearPending':
    '直前のログ消去結果が未確認のため、書き込みを一時停止しています。バッチはキューに残っています。',
  'overview.issue.spoolFull':
    'スプールが上限に達し、空き容量を待っています。滞留が続くとメッセージが破棄されることがあります。',
  'overview.issue.contract':
    '一部のバッチが書き込み検証に失敗したため、処理を停止しました。元のバッチは修復用に保存されています。',
  'overview.issue.recovery':
    '復元が必要な一時データがあるため処理を停止しました。管理者は復元状況を確認してください。',
  'overview.issue.capacity':
    'バッチがスプール容量を超えたため処理を停止しました。バッチと容量の設定を確認してください。',
  'overview.issue.sources':
    '送信元の設定を読み込めないため、新規メッセージの受信を停止しました。復旧後に再確認します。',
  'overview.issue.listenerConfig':
    '受信設定を検証できません。待ち受けアドレス、プロトコル、リソース制限を確認してください。',
  'overview.issue.tls':
    '暗号化受信を開始できません。証明書と TLS リスナー設定を確認してください。',
  'overview.issue.listener':
    '受信リスナーでエラーが発生しました。待ち受けアドレスとネットワークを確認してください。',
  'overview.issue.connection':
    '接続またはメッセージ処理で最近エラーが発生しました。送信元の設定と受信状況を確認してください。',
  'source.title': 'ログ送信元',
  'source.description':
    'デバイスの送信元を登録し、受信の有効化、時刻の解釈、ログの保存期間を設定します。',
  'source.add': '送信元を追加',
  'source.edit': '送信元を編集',
  'source.refresh': '更新',
  'source.search': '名前、IP アドレス、タイムゾーンを検索',
  'source.all': 'すべての受信設定',
  'source.enabled': '受信有効',
  'source.disabled': '受信無効',
  'source.count': '全 {total} 件 · 絞り込み {count} 件',
  'source.countUnknown': '送信元の件数 —',
  'source.name': '送信元名',
  'source.namePlaceholder': '例: コアスイッチ',
  'source.address': '送信元 IP',
  'source.addressPlaceholder': '例: 192.0.2.10',
  'source.timezone': '送信元のタイムゾーン',
  'source.timezonePlaceholder': 'IANA タイムゾーンを選択または入力',
  'source.timezoneHint':
    'タイムゾーンのないデバイス時刻を解釈するときに使います。',
  'source.retention': '保存期間',
  'source.state': '受信設定',
  'source.ignoredSeverities': '無視する重要度',
  'source.ignoreNone': 'すべての重要度を保持',
  'source.ignoreScope':
    'このルールは今後受信するログに適用されます。重要度のないログは通常どおり処理されます。',
  'source.ignoreConfirmTitle': '送信元設定の保存を確認',
  'source.ignoreMigrationRequired':
    '重要度フィルターを保存する前に、管理データベースをアップグレードしてください。',
  'source.stateHint':
    '設定が有効でもデバイスの接続を示すものではありません。送信元の設定が更新されると反映されます。',
  'source.enabledSwitch': 'この送信元からの受信を許可',
  'source.actions': '操作',
  'source.save': '保存',
  'source.cancel': 'キャンセル',
  'source.back': '編集に戻る',
  'source.saveConfirmed': '確認して保存',
  'source.empty': 'ログ送信元は未登録です',
  'source.emptyHint':
    'デバイスの送信元 IP を登録すると、ログを送信元ごとに識別して受信できます。',
  'source.noMatches': '条件に一致する送信元はありません',
  'source.loadFailed': '送信元一覧の読み込みに失敗しました',
  'source.loading': '送信元を読み込んでいます',
  'source.unavailable': '送信元は読み込みに失敗したため利用できません',
  'source.retry': '読み込みを再試行',
  'source.stale': '最後に取得できたデータを表示しています。',
  'source.required': 'この項目を入力してください',
  'source.nameMax': '送信元名は 128 文字以内にしてください',
  'source.addressInvalid':
    'IPv4 または IPv6 アドレスを入力してください。サーバー側で有効性を確認します。',
  'source.zoneMax': 'タイムゾーン名は 128 文字以内にしてください',
  'source.saveFailed': '保存できませんでした。入力は保持されています',
  'source.sourceChanged':
    '送信元の設定が更新されました。編集画面を開き直してください。',
  'retention.title': 'ログ保存',
  'retention.description':
    '送信元ごとに、新しいログを保存する日数を設定します。通常は 1～3 年です。',
  'retention.boundary':
    '期限切れのログは検索結果から除外されます。物理削除は未実装のため、期限が切れてもディスク容量は解放されません。',
  'retention.edit': '保存期間を変更',
  'retention.days': '{days} 日',
  'retention.unit': '日',
  'retention.year1': '1 年 · 365 日',
  'retention.year2': '2 年 · 730 日',
  'retention.year3': '3 年 · 1095 日',
  'retention.range': '1～3650 の整数を入力してください',
  'retention.hint': '日数で指定します。変更は今後受信するログに適用されます。',
  'retention.confirmTitle': '保存期間の変更を確認',
  'retention.confirmDescription':
    '「{name}」の保存期間を {before} 日から {after} 日に変更します。',
  'retention.confirmScope':
    '新しい期間は今後受信するログに適用されます。既存ログの有効期限は変わらず、この操作で削除されることはありません。',
  'retention.source': '現在の送信元',
  'retention.empty': '保存期間を設定できる送信元がありません',
  'retention.emptyHint': '先に「ログ送信元」でデバイスを登録してください。',
} satisfies Record<keyof typeof source, string>;
