export default {
  'listeners.legacyConfiguration':
    'サーバーの従来設定に同一プロトコルのリスナーが複数あります。サーバーでプロトコルごとに1つに整理し、サービスを再起動してください。現在の実行設定は変更されていません。',
  'listeners.failure.io':
    '待受の開始またはネットワーク入出力に失敗しました。ポートの使用状況、アドレス、権限を確認してください。',
  'listeners.failure.connectionIo':
    '直近のクライアント接続で入出力エラーが発生しました。ポートは引き続き新規接続を受け付けています。',
  'listeners.failure.tls':
    'TLS の起動に失敗しました。サーバー証明書、秘密鍵、有効期間を確認してください。',
  'listeners.failure.sources':
    'ログ送信元の設定を取得できないため、受信を一時停止しています。',
  'listeners.failure.configuration':
    '起動設定を読み込めません。管理データベースと設定バージョンを確認してください。',
  'listeners.failure.forcedDisabled':
    'サーバー設定により受信が強制的に無効です。起動設定を確認してください。',
  'menu.listeners': '受信設定',
  'listeners.title': '受信設定',
  'listeners.description': 'UDP、TCP、TLS、RELP のログ受信を設定します。',
  'listeners.reload': '設定を再読み込み',
  'listeners.restartHint':
    '設定を保存した後、対象のプロトコルまたはログ受信のメインスイッチを入れ直すと適用されます。',
  'listeners.boundary':
    'メインスイッチをオフにすると、すべての待ち受けと接続が停止します。オフの間も各プロトコルの有効状態を保存できます。',
  'listeners.loadFailed': '受信設定を読み込めませんでした',
  'listeners.saved': '設定を保存しました',
  'listeners.running': '現在の実行設定',
  'listeners.pending': '設定の適用待ち',
  'listeners.current': '設定を読み込み済み',
  'listeners.master': 'ログ受信',
  'listeners.masterHint':
    'メインスイッチがオンの間は、切り替えたプロトコルだけに影響します。',
  'listeners.protocol': 'プロトコル',
  'listeners.address': '待受アドレス',
  'listeners.port': 'ポート',
  'listeners.state': '待受状態',
  'listeners.failure': '問題',
  'listeners.listenerFailed':
    '受信に問題があります。アドレス、ポート、証明書設定を確認してください。',
  'listeners.savedConfiguration': '保存する受信設定',
  'listeners.enabled': 'このプロトコルを有効化',
  'listeners.addressRequired':
    'IPv4 または IPv6 の待受アドレスを入力してください。',
  'listeners.portRequired': 'ポートは 1～65535 の整数で指定してください。',
  'listeners.framing': 'メッセージ区切り',
  'listeners.connectionErrors': 'クライアント接続エラー',
  'listeners.clientAddress': 'クライアント IP',
  'listeners.errorOccurredAt': '発生時刻',
  'listeners.auto': '自動検出（バイト数指定 / 改行）',
  'listeners.octet': 'バイト数指定',
  'listeners.newline': '改行区切り（LF / CRLF）',
  'listeners.certificate': 'TLS 証明書',
  'listeners.certificateRequired':
    'TLS を有効にするには利用可能なサーバー証明書を選択してください。',
  'listeners.noCertificates':
    'サーバー証明書が未設定です。サーバーで証明書を設定して再起動してください。この画面では秘密鍵やパスワードを入力しません。',
  'listeners.frameTimeout': 'フレームタイムアウト（秒）',
  'listeners.idleTimeout': 'アイドルタイムアウト（秒）',
  'listeners.timeoutRequired': '許容範囲内の整数秒を入力してください。',
  'listeners.relpHint':
    'RELP の確認応答は受信側での受付を示し、ログ DB への保存完了ではありません。未登録機器は最新 100 件をメモリ内に保持し、再起動で消去します。',
  'listeners.save': '受信設定を保存',
  'listeners.confirmTitle': '受信設定の保存確認',
  'listeners.confirmSave': '保存を確定',
  'listeners.confirmHint':
    '機器の送信プロトコル、宛先アドレス、ポートを確認してください。保存後、対象のプロトコルまたはメインスイッチを入れ直してください。',
  'listeners.saveFailed': '保存結果を確認できませんでした',
  'listeners.conflict':
    '他の管理者が設定を変更しました。キャンセルして再読み込みしてください。',
  'listeners.migrationRequired':
    '管理 DB の受信設定テーブルを明示的に移行する必要があります。管理者に依頼してください。',
  'listeners.invalid':
    '受信設定が無効です。アドレス、ポート、タイムアウト範囲を確認してください。',
  'listeners.portConflict':
    '有効なリスナーのアドレスまたはポートが競合しています。修正してください。',
  'listeners.state.disabled': '無効',
  'listeners.state.starting': '起動中',
  'listeners.state.listening': '待受中',
  'listeners.state.failed': '起動・受信エラー',
  'listeners.state.stopped': '停止済み',
  'listeners.state.unknown': '不明',
  'listeners.confirmEnable': 'ログ受信を開始しますか？',
  'listeners.confirmDisable': 'ログ受信を停止しますか？',
  'listeners.confirmSwitch': '切り替える',
  'listeners.enableHint':
    '最新の保存済み設定を読み込み、有効なプロトコルのポートで受信を開始します。',
  'listeners.disableHint': 'ログ受信ポートを閉じ、既存の受信接続を切断します。',
  'listeners.switchFailed':
    'ログ受信の状態を確認できませんでした。設定を再読み込みしてください。',
  'listeners.protocolEnable': 'プロトコルを有効にしますか？',
  'listeners.protocolDisable': 'プロトコルを無効にしますか？',
  'listeners.protocolEnableHint':
    '最後に保存した設定で、このプロトコルの待ち受けを開始します。',
  'listeners.protocolDisableHint':
    'このプロトコルの待ち受けを停止し、既存の接続を切断します。',
  'listeners.protocolSavedHint':
    'メインスイッチがオフのため、このプロトコルの有効状態だけを保存します。',
  'listeners.streamInterrupted':
    'リアルタイム接続が切断されました。最後に取得した状態を表示しています。',
};
