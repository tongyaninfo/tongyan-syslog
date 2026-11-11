import type source from '../zh-CN/users';

export default {
  'menu.users': 'ユーザー管理',
  'users.title': 'ユーザー管理',
  'users.description':
    '管理画面のアカウント、有効状態、パスワードを設定します。',
  'users.permissionHint':
    '現在のアカウントはすべて管理者です。すべてのログの閲覧とシステム設定の変更ができます。',
  'users.search': 'ユーザー名で検索',
  'users.filter': 'アカウントの状態',
  'users.all': 'すべての状態',
  'users.enabled': '有効',
  'users.disabled': '無効',
  'users.username': 'ユーザー名',
  'users.role': 'ロール',
  'users.admin': '管理者',
  'users.createdAt': '作成日時',
  'users.actions': '操作',
  'users.current': '現在のアカウント',
  'users.add': 'ユーザーを追加',
  'users.edit': 'ユーザーを編集',
  'users.enable': '有効化',
  'users.disable': '無効化',
  'users.resetPassword': 'パスワードをリセット',
  'users.refresh': '更新',
  'users.total': '全 {total} アカウント',
  'users.empty': '条件に一致するアカウントはありません',
  'users.unavailable': 'アカウント一覧を一時的に利用できません',
  'users.loadFailed': 'アカウントを読み込めませんでした',
  'users.stale':
    '前回取得した結果を表示しています。更新してから操作してください。',
  'users.usernameRule':
    '英数字、ピリオド、ハイフン、アンダースコアを 3～64 文字使用してください。',
  'users.passwordRule':
    'パスワードは 8～1024 文字とし、空白のみにはできません。',
  'users.confirmPassword': 'パスワードの確認',
  'users.passwordMismatch': '入力したパスワードが一致しません。',
  'users.saveFailed':
    '保存できませんでした。メッセージを確認して再試行してください。',
  'users.created': 'ユーザーを作成しました。',
  'users.updated': 'アカウント設定を保存しました。',
  'users.passwordReset':
    'パスワードをリセットしました。新しいパスワードでログインし直す必要があります。',
  'users.selfChanged':
    'アカウント情報を変更しました。ログインし直してください。',
  'users.editHint':
    'ユーザー名を変更すると、このアカウントの全セッションが終了します。',
  'users.selfHint':
    '現在のアカウントは無効化できません。自分のパスワードは右上のアカウントメニューから変更してください。',
  'users.disableTitle': 'アカウントの無効化を確認',
  'users.disableHint':
    '{username} を無効化すると、ログインできなくなり、既存の全セッションが直ちに終了します。',
  'users.resetTitle': 'パスワードのリセットを確認',
  'users.resetHint':
    '{username} のパスワードをリセットすると、そのアカウントの全セッションが終了します。',
  'users.confirm': '実行する',
  'users.back': '編集に戻る',
  'users.previewHint':
    'このプレビューはアカウント操作のデモです。実際のパスワードは保存されません。サンプルアカウントは preview-only でログインします。',
  'setup.title': '管理者の初期設定',
  'setup.description':
    'Tongyan Syslog へようこそ。管理者アカウントを作成してログセンターの設定を始めましょう。',
  'setup.cardTitle': '最初の管理者を作成',
  'setup.cardHint':
    'アカウントがまだ設定されていません。ユーザー名とパスワードを設定してログイン画面に進みます。',
  'setup.submit': '管理者を作成して続行',
  'setup.restricted': 'サーバー本体から初期設定を行ってください',
  'setup.restrictedHint':
    '最初の管理者はサーバー本体から HTTPS でのみ作成できます。リモート設置時には管理されたトンネルを使用できます。設定後は通常どおりリモートでログインできます。',
  'setup.unavailable': '初期設定の状態を取得できません',
  'setup.unavailableHint':
    'システムの状態を確認できていません。再試行するかサービス設定を確認してください。この段階ではアカウントやストレージを変更しません。',
  'setup.completed':
    '管理者を作成しました。設定したアカウントでログインしてください。',
  'setup.previewCompleted':
    '初期設定のプレビューが完了しました。設定したサンプルユーザー名と preview-only でログインしてください。',
  'setup.previewHint':
    'これは初期設定のデモです。サンプルのユーザー名だけを使用してください。入力したパスワードは送信も保存もされません。設定後はサンプルユーザー名と preview-only でログインします。',
  'setup.loginHint':
    '初回利用時に管理者の作成を案内します。以降のアカウントは管理者が設定します。',
  'error.usernameConflict':
    'このユーザー名はすでに使用されています。別の名前にしてください。',
  'error.userVersion':
    '別の操作でアカウントが変更されました。一覧を更新してから編集し直してください。',
  'error.lastAdmin':
    '有効な管理者アカウントを少なくとも 1 つ残す必要があります。',
  'error.selfDisable': '現在ログインしているアカウントは無効化できません。',
  'error.selfReset':
    '自分のパスワードは右上のアカウントメニューから変更してください。',
  'error.setupCompleted':
    '管理者はすでに設定されています。ログイン画面に戻ってください。',
  'error.setupLocal': 'サーバー本体の安全な接続から初期設定を行ってください。',
} satisfies Record<keyof typeof source, string>;
