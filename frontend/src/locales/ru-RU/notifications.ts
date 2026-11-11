export default {
  'menu.notifications': 'Уведомления',
  'notifications.description':
    'Настройте почту и подписки на отчёты по журналам и просматривайте историю отправки.',
  'notifications.smtp': 'Почтовая служба',
  'notifications.reports': 'Подписки на отчёты',
  'notifications.history': 'История отправки',
  'notifications.host': 'Сервер SMTP',
  'notifications.port': 'Порт SMTP',
  'notifications.useTls': 'Включить SSL/TLS',
  'notifications.security': 'Режим шифрования',
  'notifications.securityHint':
    'Обычно SSL/TLS использует порт 465, а STARTTLS — 587.',
  'notifications.unencrypted':
    'Без шифрования доступны только внутренние ретрансляторы SMTP без аутентификации.',
  'notifications.username': 'Имя пользователя SMTP',
  'notifications.password': 'Код авторизации SMTP',
  'notifications.passwordHint':
    'Используйте код от почтовой службы. Для ретранслятора без аутентификации поле можно оставить пустым.',
  'notifications.keepPassword':
    'Код сохранён. Оставьте поле пустым, чтобы сохранить его, либо введите код заново при смене подключения или учётной записи.',
  'notifications.senderAddress': 'Адрес отправителя',
  'notifications.senderName': 'Имя отправителя',
  'notifications.subjectPrefix': 'Префикс темы',
  'notifications.subjectPrefixHint':
    'Применяется к тестовым письмам и отчётам по расписанию. Оставьте пустым, чтобы убрать префикс.',
  'notifications.subjectPreview': 'Предпросмотр темы',
  'notifications.smtpSaved': 'Настройки почты сохранены.',
  'notifications.testMail': 'Тестовое письмо',
  'notifications.testHint':
    'Тестовые письма используют сохранённые настройки почты.',
  'notifications.recipient': 'Адрес получателя',
  'notifications.sendTest': 'Отправить тестовое письмо',
  'notifications.testQueued': 'Тестовое письмо поставлено в очередь.',
  'notifications.invalidAddress':
    'Введите действительный адрес электронной почты.',
  'notifications.addReport': 'Добавить подписку',
  'notifications.editReport': 'Изменить подписку',
  'notifications.edit': 'Изменить',
  'notifications.reportName': 'Название подписки',
  'notifications.frequency': 'Периодичность',
  'notifications.sendTime': 'Время отправки',
  'notifications.scheduleHint':
    'Расписание использует время Asia/Shanghai. Еженедельные отчёты отправляются по понедельникам, ежемесячные — первого числа.',
  'notifications.allSourcesHint':
    'Оставьте пустым, чтобы включить все зарегистрированные источники.',
  'notifications.recipients': 'Получатели',
  'notifications.recipientsHint':
    'Нажмите Enter или введите запятую, чтобы добавить адрес. Допускается до 20 получателей.',
  'notifications.reportState': 'Состояние подписки',
  'notifications.enabled': 'Включена',
  'notifications.disabled': 'Отключена',
  'notifications.nextSend': 'Следующая отправка (UTC+8)',
  'notifications.sourceCount': 'Источников: {count}',
  'notifications.noReports': 'Нет подписок на отчёты',
  'notifications.mailKind': 'Тип письма',
  'notifications.subject': 'Тема',
  'notifications.deliveryStatus': 'Статус отправки',
  'notifications.createdAt': 'Время создания',
  'notifications.updatedAt': 'Время обновления',
  'notifications.attempts': 'Попытки отправки',
  'notifications.period': 'Период отчёта',
  'notifications.failureCode': 'Код ошибки',
  'notifications.filter': 'Применить фильтры',
  'notifications.noDeliveries': 'Нет записей об отправке',
  'notifications.deliveryDetail': 'Сведения об отправке',
  'notifications.htmlPreview': 'Предпросмотр письма',
  'notifications.textPreview': 'Обычный текст',
  'notifications.noBody': 'Текст письма для просмотра отсутствует.',
  'notifications.kind.test': 'Тестовое письмо',
  'notifications.kind.daily': 'Ежедневный отчёт',
  'notifications.kind.weekly': 'Еженедельный отчёт',
  'notifications.kind.monthly': 'Ежемесячный отчёт',
  'notifications.status.queued': 'В очереди',
  'notifications.status.sending': 'Отправляется',
  'notifications.status.accepted': 'Принято SMTP',
  'notifications.status.failed': 'Ошибка отправки',
  'notifications.status.unknown': 'Результат неизвестен',
  'notifications.migrationRequired':
    'Перед использованием уведомлений обновите управляющую базу данных.',
  'notifications.smtpNotConfigured':
    'Сначала сохраните настройки почтовой службы.',
  'notifications.passwordRequired':
    'Введите код авторизации SMTP для этого подключения.',
  'notifications.conflict':
    'Настройки изменились. Обновите их перед сохранением.',
  'notifications.queueFull':
    'Очередь отправки заполнена. Повторите попытку позже.',
  'notifications.subscriptionLimit': 'Достигнут предел подписок на отчёты.',
  'notifications.authenticationFailed':
    'Ошибка аутентификации SMTP. Проверьте имя пользователя и код авторизации.',
  'notifications.providerRejected':
    'Сервер SMTP отклонил письмо. Проверьте отправителя, получателя и ограничения службы.',
  'notifications.tlsFailed':
    'Не удалось установить шифрованное соединение. Проверьте порт, режим шифрования и сертификат сервера.',
  'notifications.smtpConnectionFailed':
    'Не удалось подключиться к серверу SMTP. Проверьте адрес, порт и сеть.',
  'notifications.reportUnavailable':
    'Не удалось создать отчёт. Проверьте состояние хранилища журналов.',
  'notifications.subscriptionChanged':
    'Настройки подписки изменились до отправки предыдущего отчёта.',
};
