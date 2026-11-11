export default {
  'menu.notifications': '通知設定',
  'notifications.description':
    'メールとログレポートの配信を設定し、送信履歴を確認できます。',
  'notifications.smtp': 'メールサービス',
  'notifications.reports': 'レポート購読',
  'notifications.history': '送信履歴',
  'notifications.host': 'SMTP サーバー',
  'notifications.port': 'SMTP ポート',
  'notifications.useTls': 'SSL/TLS を有効にする',
  'notifications.security': '暗号化方式',
  'notifications.securityHint':
    '通常、SSL/TLS は 465、STARTTLS は 587 ポートを使います。',
  'notifications.unencrypted':
    '暗号化を無効にすると認証不要の内部 SMTP リレーのみ利用できます。',
  'notifications.username': 'SMTP ユーザー名',
  'notifications.password': 'SMTP 認証コード',
  'notifications.passwordHint':
    'メールサービスが発行した認証コードを使用します。認証不要なら空欄にします。',
  'notifications.keepPassword':
    '認証コードは保存済みです。引き続き使うには空欄にし、接続情報やアカウントを変える場合は再入力してください。',
  'notifications.senderAddress': '差出人メールアドレス',
  'notifications.senderName': '差出人名',
  'notifications.subjectPrefix': '件名の接頭辞',
  'notifications.subjectPrefixHint':
    'テストメールと定期レポートに適用します。空欄にすると接頭辞を付けません。',
  'notifications.subjectPreview': '件名のプレビュー',
  'notifications.smtpSaved': 'メール設定を保存しました。',
  'notifications.testMail': 'テストメール',
  'notifications.testHint': 'テストメールには保存済みの設定を使用します。',
  'notifications.recipient': '宛先メールアドレス',
  'notifications.sendTest': 'テストメールを送信',
  'notifications.testQueued': 'テストメールを送信キューに追加しました。',
  'notifications.invalidAddress': '有効なメールアドレスを入力してください。',
  'notifications.addReport': '購読を追加',
  'notifications.editReport': '購読を編集',
  'notifications.edit': '編集',
  'notifications.reportName': '購読名',
  'notifications.frequency': '送信頻度',
  'notifications.sendTime': '送信時刻',
  'notifications.scheduleHint':
    'スケジュールは上海時間です。週報は月曜日、月報は毎月 1 日に送信します。',
  'notifications.allSourcesHint':
    '空欄の場合、登録済みの全ソースを集計します。',
  'notifications.recipients': '宛先',
  'notifications.recipientsHint':
    'Enter またはカンマでアドレスを追加します。最大 20 件です。',
  'notifications.reportState': '購読状態',
  'notifications.enabled': '有効',
  'notifications.disabled': '無効',
  'notifications.nextSend': '次回送信（UTC+8）',
  'notifications.sourceCount': '{count} 件のソース',
  'notifications.noReports': 'レポート購読がありません',
  'notifications.mailKind': 'メール種別',
  'notifications.subject': '件名',
  'notifications.deliveryStatus': '送信状態',
  'notifications.createdAt': '作成日時',
  'notifications.updatedAt': '更新日時',
  'notifications.attempts': '送信試行回数',
  'notifications.period': 'レポート対象期間',
  'notifications.failureCode': 'エラーコード',
  'notifications.filter': '絞り込み',
  'notifications.noDeliveries': '送信履歴がありません',
  'notifications.deliveryDetail': '送信の詳細',
  'notifications.htmlPreview': 'メールプレビュー',
  'notifications.textPreview': 'プレーンテキスト',
  'notifications.noBody': 'プレビューできるメール本文がありません。',
  'notifications.kind.test': 'テストメール',
  'notifications.kind.daily': '日報',
  'notifications.kind.weekly': '週報',
  'notifications.kind.monthly': '月報',
  'notifications.status.queued': '待機中',
  'notifications.status.sending': '送信中',
  'notifications.status.accepted': 'SMTP 受付済み',
  'notifications.status.failed': '送信失敗',
  'notifications.status.unknown': '結果不明',
  'notifications.migrationRequired':
    '通知を使用する前に管理データベースをアップグレードしてください。',
  'notifications.smtpNotConfigured':
    '先にメールサービスの設定を保存してください。',
  'notifications.passwordRequired':
    'この接続に対応する SMTP 認証コードを入力してください。',
  'notifications.conflict':
    '設定が変更されました。再読み込みしてから保存してください。',
  'notifications.queueFull':
    '送信キューがいっぱいです。しばらくしてから再試行してください。',
  'notifications.subscriptionLimit': 'レポート購読数が上限に達しました。',
  'notifications.authenticationFailed':
    'SMTP 認証に失敗しました。ユーザー名と認証コードを確認してください。',
  'notifications.providerRejected':
    'SMTP サーバーがメールを拒否しました。差出人、宛先、サービスの制限を確認してください。',
  'notifications.tlsFailed':
    '暗号化接続を確立できませんでした。ポート、暗号化方式、サーバー証明書を確認してください。',
  'notifications.smtpConnectionFailed':
    'SMTP サーバーに接続できませんでした。アドレス、ポート、ネットワークを確認してください。',
  'notifications.reportUnavailable':
    'レポートを生成できませんでした。ログストレージの状態を確認してください。',
  'notifications.subscriptionChanged': '送信前に購読設定が変更されました。',
};
