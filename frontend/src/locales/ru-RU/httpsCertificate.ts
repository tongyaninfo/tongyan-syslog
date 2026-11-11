export default {
  'menu.httpsCertificate': 'Сертификат HTTPS',
  'httpsCertificate.title': 'Сертификат HTTPS консоли',
  'httpsCertificate.current': 'Действующий сертификат',
  'httpsCertificate.pending': 'Сертификат для следующего запуска',
  'httpsCertificate.noCurrent':
    'Сведения о действующем сертификате недоступны.',
  'httpsCertificate.noPending': 'Нет сертификата для следующего запуска.',
  'httpsCertificate.subject': 'Субъект',
  'httpsCertificate.fingerprint': 'Отпечаток',
  'httpsCertificate.notBefore': 'Действует с (UTC+8)',
  'httpsCertificate.notAfter': 'Действует до (UTC+8)',
  'httpsCertificate.upload': 'Загрузка сертификата',
  'httpsCertificate.file': 'Файл сертификата',
  'httpsCertificate.chooseFile': 'Выбрать файл сертификата',
  'httpsCertificate.fileHint':
    'Выберите файл .pfx или .p12 с закрытым ключом размером до 256 КиБ.',
  'httpsCertificate.password': 'Пароль сертификата (необязательно)',
  'httpsCertificate.readPasswordFile': 'Прочитать файл пароля',
  'httpsCertificate.passwordFileInvalid':
    'Выберите файл TXT в кодировке UTF-8 размером до 4 КиБ. Пароль должен занимать одну строку длиной до 1024 символов.',
  'httpsCertificate.save': 'Сохранить сертификат',
  'httpsCertificate.confirmTitle': 'Сохранить сертификат HTTPS?',
  'httpsCertificate.confirmHint':
    'После сохранения новые HTTPS-подключения к консоли автоматически будут использовать новый сертификат.',
  'httpsCertificate.pendingNotice': 'Сертификат сохранён и ожидает активации.',
  'httpsCertificate.saved':
    'Новые HTTPS-соединения используют обновлённый сертификат. Открытые соединения используют прежний сертификат до закрытия. Для проверки перезапустите браузер и откройте адрес по имени хоста или IP-адресу из сертификата.',
  'httpsCertificate.loadFailed': 'Не удалось загрузить сохранённый сертификат',
  'httpsCertificate.fileRequired': 'Выберите допустимый файл .pfx или .p12.',
  'httpsCertificate.tooLarge':
    'Размер файла сертификата не должен превышать 256 КиБ.',
  'httpsCertificate.invalid':
    'Недействительный сертификат или пароль. Проверьте наличие рабочего закрытого ключа и срок действия сертификата.',
  'httpsCertificate.unavailable':
    'Сертификат недоступен. Проверьте настройки сервера.',
  'httpsCertificate.managedExternally':
    'Сертификат HTTPS управляется настройками сервера. Обновите его на сервере.',
  'httpsCertificate.selfSigned': 'Самоподписанный сертификат HTTPS службы',
  'httpsCertificate.uploaded': 'Загруженный сертификат HTTPS',
};
