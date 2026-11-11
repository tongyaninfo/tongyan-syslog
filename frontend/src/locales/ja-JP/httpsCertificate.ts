export default {
  'menu.httpsCertificate': 'HTTPS 証明書',
  'httpsCertificate.title': '管理コンソールの HTTPS 証明書',
  'httpsCertificate.current': '現在の証明書',
  'httpsCertificate.pending': '適用待ちの証明書',
  'httpsCertificate.noCurrent': '現在の証明書情報を取得できません。',
  'httpsCertificate.noPending': '適用待ちの証明書はありません。',
  'httpsCertificate.subject': 'サブジェクト',
  'httpsCertificate.fingerprint': 'フィンガープリント',
  'httpsCertificate.notBefore': '有効期間の開始（UTC+8）',
  'httpsCertificate.notAfter': '有効期限（UTC+8）',
  'httpsCertificate.upload': '証明書のアップロード',
  'httpsCertificate.file': '証明書ファイル',
  'httpsCertificate.chooseFile': '証明書ファイルを選択',
  'httpsCertificate.fileHint':
    '秘密鍵を含む 256 KiB 以下の .pfx または .p12 ファイルを選択してください。',
  'httpsCertificate.password': '証明書のパスワード（任意）',
  'httpsCertificate.readPasswordFile': 'パスワードファイルを読み込む',
  'httpsCertificate.passwordFileInvalid':
    '4 KiB 以下の UTF-8 TXT ファイルを選択してください。パスワードは 1 行、最大 1024 文字です。',
  'httpsCertificate.save': '証明書を保存',
  'httpsCertificate.confirmTitle': 'HTTPS 証明書を保存しますか？',
  'httpsCertificate.confirmHint':
    '保存後、管理コンソールへの新しい HTTPS 接続には新しい証明書が自動的に適用されます。',
  'httpsCertificate.pendingNotice': '保存した証明書は適用待ちです。',
  'httpsCertificate.saved':
    '新しい HTTPS 接続には更新後の証明書が使われます。既存の接続は切断されるまで以前の証明書を使います。確認するにはブラウザーを再起動し、証明書の対象となるホスト名または IP アドレスでアクセスしてください。',
  'httpsCertificate.loadFailed': '保存済みの証明書を読み込めませんでした',
  'httpsCertificate.fileRequired':
    '有効な .pfx または .p12 ファイルを選択してください。',
  'httpsCertificate.tooLarge': '証明書ファイルは 256 KiB 以下にしてください。',
  'httpsCertificate.invalid':
    '証明書またはパスワードが無効です。利用可能な秘密鍵が含まれ、証明書が有効期間内か確認してください。',
  'httpsCertificate.unavailable':
    '証明書を利用できません。サーバー設定を確認してください。',
  'httpsCertificate.managedExternally':
    'HTTPS 証明書はサーバー設定で管理されています。サーバー側で更新してください。',
  'httpsCertificate.selfSigned': 'サービスの自己署名 HTTPS 証明書',
  'httpsCertificate.uploaded': 'アップロードした HTTPS 証明書',
};
