import httpsCertificate from './ru-RU/httpsCertificate';
import listeners from './ru-RU/listeners';
import liveLogs from './ru-RU/liveLogs';
import mcp from './ru-RU/mcp';
import notifications from './ru-RU/notifications';
import unregistered from './ru-RU/unregistered';
import users from './ru-RU/users';
import type source from './zh-CN';

export default {
  ...liveLogs,
  ...mcp,
  ...notifications,
  ...httpsCertificate,
  ...listeners,
  ...unregistered,
  ...users,
  'app.title': 'Tongyan Syslog',
  'app.brand': 'Tongyan Technology',
  'app.description': 'Платформа централизованного управления журналами',
  'app.logout': 'Выйти',
  'app.logoutFailed':
    'Выйти не удалось. Сеанс всё ещё активен. Повторите попытку.',
  'app.theme': 'Режим оформления',
  'app.light': 'Светлая',
  'app.dark': 'Тёмная',
  'app.auto': 'Авто',
  'app.account': 'Администратор',
  'app.preview': 'Предпросмотр на синтетических данных',
  'app.sessionLoading': 'Проверка сеанса…',
  'app.retry': 'Перезагрузить',
  'app.notFound': 'Страница не найдена',
  'app.back': 'Вернуться к обзору',
  'app.timezone': 'Пекинское время · UTC+8',
  'app.appearance': 'Настройки темы',
  'app.appearanceStyle': 'Стиль оформления',
  'app.autoHint':
    'Автоматический режим использует светлую или тёмную тему системы.',
  'app.language': 'Язык интерфейса',
  'app.userMenu': 'Меню учётной записи',
  'app.changePassword': 'Изменить пароль',
  'app.currentPassword': 'Текущий пароль',
  'app.newPassword': 'Новый пароль',
  'app.confirmPassword': 'Подтвердите новый пароль',
  'app.passwordRequired': 'Введите пароль.',
  'app.passwordLength': 'Новый пароль должен содержать от 8 до 1024 символов.',
  'app.passwordMismatch': 'Новые пароли не совпадают.',
  'app.passwordChanged': 'Пароль изменён. Войдите снова.',
  'app.passwordHint': 'После изменения пароля все активные сеансы завершатся.',
  'app.passwordPreviewHint':
    'В предпросмотре можно проверить форму и повторный вход. Пароль остаётся прежним.',
  'app.passwordPreviewChanged':
    'Предпросмотр завершён. Войдите с прежним паролем для предпросмотра.',
  'app.cancel': 'Отмена',
  'app.save': 'Сохранить',
  'login.welcome': 'С возвращением',
  'login.subtitle':
    'Войдите, чтобы просматривать журналы устройств и состояние приёма.',
  'login.hero': 'Проследите путь каждой записи',
  'login.description':
    'Собирайте журналы устройств и приложений, ищите и анализируйте события в одном интерфейсе.',
  'login.collect': 'Централизованный приём',
  'login.collectDescription':
    'Собирайте Syslog с коммутаторов, ESXi и приложений',
  'login.search': 'Быстрый поиск',
  'login.searchDescription':
    'Находите события по времени, источнику, уровню и ключевым словам',
  'login.evidence': 'Просмотр оригинала',
  'login.evidenceDescription':
    'Сопоставляйте структурированные поля с исходным сообщением',
  'login.username': 'Имя пользователя',
  'login.password': 'Пароль',
  'login.usernameRequired': 'Введите имя пользователя',
  'login.passwordRequired': 'Введите пароль',
  'login.submit': 'Войти в консоль',
  'login.sessionHint':
    'Для доступа используется защищённый сеанс. Консоль не хранит пароль в браузере.',
  'login.initialAccount':
    'Учётную запись заранее создаёт системный администратор.',
  'parse.parsed': 'Разобрано',
  'parse.failed': 'Ошибка разбора',
  'parse.unknown': 'Формат не распознан',
  'severity.0': 'Чрезвычайная ситуация',
  'severity.1': 'Тревога',
  'severity.2': 'Критический',
  'severity.3': 'Ошибка',
  'severity.4': 'Предупреждение',
  'severity.5': 'Уведомление',
  'severity.6': 'Информация',
  'severity.7': 'Отладка',
  'error.generic': 'Сервис временно недоступен. Повторите попытку позже.',
  'error.auth': 'Неверное имя пользователя или пароль.',
  'error.session': 'Сеанс завершён. Войдите снова.',
  'error.input': 'Проверьте введённые данные и повторите попытку.',
  'error.query': 'Проверьте период запроса, размер страницы и фильтры.',
  'error.token':
    'Поиск поддерживает только целые слова из букв ASCII и цифр с учётом регистра.',
  'error.permission': 'У этой учётной записи нет прав на операцию.',
  'error.notFound': 'Запись отсутствует, просрочена или недоступна.',
  'error.conflict':
    'Этот IP источника уже зарегистрирован. Проверьте дубликаты.',
  'error.csrf':
    'Не удалось проверить источник запроса. Войдите с исходного адреса консоли.',
  'error.https':
    'Для входа нужно защищённое соединение. Откройте консоль по HTTPS.',
  'error.rate': 'Слишком много запросов. Повторите попытку позже.',
  'error.timeout':
    'Время ожидания истекло. Повторите попытку или сузьте диапазон поиска.',
  'error.reconcile':
    'Требуется сверка данных в хранилище. Обратитесь к администратору.',
  'error.network':
    'Нет соединения с сервисом. Проверьте сеть и повторите попытку.',
  'error.currentPassword': 'Текущий пароль неверен.',
  'error.passwordInvalid': 'Новый пароль не соответствует требованиям.',
  'error.passwordUnchanged': 'Новый пароль должен отличаться от текущего.',
  'error.tooManyRequests': 'Слишком много запросов. Повторите попытку позже.',
  'logs.title': 'Поиск журналов',
  'logs.subtitle':
    'Ищите журналы по времени и источнику, просматривайте результаты разбора и исходные сообщения.',
  'logs.range': 'Время приёма',
  'logs.timezone': 'Часовой пояс Asia/Shanghai',
  'logs.rangeHint':
    'За один раз можно искать за период до 30 дней в пределах срока хранения.',
  'logs.lastHour': 'За час',
  'logs.lastDay': 'За 24 часа',
  'logs.lastWeek': 'За 7 дней',
  'logs.lastMonth': 'За 30 дней',
  'logs.source': 'Источник журнала',
  'logs.allSources': 'Все зарегистрированные источники',
  'logs.severity': 'Уровень',
  'logs.allSeverities': 'Все уровни',
  'logs.hostname': 'Имя хоста',
  'logs.hostnamePlaceholder': 'Точное совпадение с именем хоста в сообщении',
  'logs.appName': 'Имя приложения',
  'logs.appNamePlaceholder': 'Точное имя приложения',
  'logs.keywords': 'Ключевые слова в тексте',
  'logs.keywordsPlaceholder': 'Например, LINK DOWN',
  'logs.keywordHint':
    'Совпадение по целым словам из букв ASCII и цифр с учётом регистра. Все слова должны присутствовать. Другие символы и подстроки не поддерживаются.',
  'logs.pageSize': 'Записей на странице',
  'logs.pageSizeValue': '{count}',
  'logs.search': 'Найти записи',
  'logs.clearAction': 'Очистить журналы',
  'logs.clearTitle': 'Очистить все сохранённые журналы',
  'logs.clearSourceTitle': 'Очистить журналы этого устройства',
  'logs.clearSourceScope':
    'Сохранённые журналы устройства {name} ({address}) будут удалены.',
  'logs.clearSourceSucceeded': 'Журналы устройства {name} удалены.',
  'logs.clearSubmit': 'Подтвердить очистку',
  'logs.clearScope':
    'Все записи Syslog, сохранённые в ClickHouse, будут удалены без возможности восстановления. Очистка затронет и записи за пределами текущего фильтра.',
  'logs.clearPendingLogs':
    'Приём и сохранение журналов продолжатся после очистки. Записи из очереди могут появиться позже.',
  'logs.clearConfirmation': 'Введите CLEAR для подтверждения',
  'logs.clearSucceeded': 'Журналы очищены.',
  'logs.clearInProgress':
    'Уже выполняется другой запрос очистки. Дождитесь его завершения и обновите результаты поиска.',
  'logs.clearSizeLimit':
    'Размер таблицы журналов превышает лимит для очистки. Попросите администратора проверить настройки ClickHouse.',
  'logs.clearOutcomeUnknown':
    'Не удалось подтвердить результат очистки. Проверьте его поиском перед повторным запросом.',
  'logs.clearStorageAccess':
    'Хранилище журналов отклонило запрос очистки. Попросите администратора проверить права учётной записи хранилища.',
  'logs.clearFailed': 'Запрос очистки не завершён. Проверьте состояние службы.',
  'logs.reset': 'Сбросить',
  'logs.refresh': 'Повторить поиск',
  'logs.results': 'Результаты поиска',
  'logs.resultsHint':
    'Совпадения показаны от новых записей к старым по времени приёма.',
  'logs.page': 'Страница {page}',
  'logs.rows': 'На странице: {count}',
  'logs.previous': 'Предыдущая',
  'logs.next': 'Следующая',
  'logs.noMore': 'Конец результатов',
  'logs.empty': 'Совпадений нет',
  'logs.emptyHint':
    'Измените время, источник или ключевые слова и повторите поиск.',
  'logs.pending': 'Фильтры изменены',
  'logs.pendingHint': 'Нажмите «Найти записи», чтобы применить условия.',
  'logs.loading': 'Выполняется поиск',
  'logs.queryFailed': 'Поиск не завершён',
  'logs.pageFailed': 'Не удалось загрузить страницу {page}',
  'logs.restartFromFirstPage': 'Повторить поиск с первой страницы',
  'logs.error.storageConnection':
    'Не удалось подключиться к хранилищу журналов. Проверьте, запущен ли ClickHouse и верны ли параметры подключения, затем повторите запрос.',
  'logs.error.storageAccess':
    'Хранилище журналов отказало в доступе. Попросите администратора проверить учётную запись хранилища и права чтения.',
  'logs.error.storageSchema':
    'Отсутствует необходимая база данных или таблица журналов. Попросите администратора проверить инициализацию хранилища.',
  'logs.error.storageSchemaIncompatible':
    'Существующая таблица журналов ClickHouse несовместима. Попросите администратора проверить её определение.',
  'logs.error.storageTimeout':
    'Время ожидания запроса к хранилищу журналов истекло. Попробуйте сократить период поиска.',
  'logs.error.storageUnavailable':
    'Хранилище журналов не завершило запрос. Проверьте его состояние и повторите попытку.',
  'logs.error.invalidResponse':
    'Не удалось прочитать ответ службы журналов. Повторите запрос и обратитесь к администратору, если ошибка повторяется.',
  'logs.error.queryUnavailable':
    'Поиск журналов временно недоступен. Повторите запрос с текущими фильтрами и обратитесь к администратору, если ошибка повторяется.',
  'logs.sourcesFailed': 'Список источников временно недоступен',
  'logs.sourcesRetry': 'Перезагрузить источники',
  'logs.noSources': 'Источники журналов не зарегистрированы',
  'logs.noSourcesHint':
    'Сначала зарегистрируйте адрес устройства в разделе источников, затем настройте отправку журналов.',
  'logs.receivedAt': 'Время приёма',
  'logs.eventTime': 'Время устройства',
  'logs.message': 'Текст журнала',
  'logs.parseStatus': 'Статус разбора',
  'logs.parserVersion': 'Версия парсера',
  'logs.action': 'Действие',
  'logs.detail': 'Подробнее',
  'logs.detailTitle': 'Детали записи',
  'logs.detailLoading': 'Загрузка деталей записи',
  'logs.detailFailed': 'Не удалось прочитать запись',
  'logs.close': 'Закрыть',
  'logs.retry': 'Повторить',
  'logs.eventId': 'ID события',
  'logs.sourceId': 'ID источника',
  'logs.peerAddress': 'Адрес подключения',
  'logs.transport': 'Транспортный протокол',
  'logs.expiresAt': 'Хранить до',
  'logs.timeConfidence': 'Интерпретация времени',
  'logs.originalTimestamp': 'Исходная метка времени',
  'logs.procId': 'ID процесса',
  'logs.msgId': 'ID сообщения',
  'logs.facility': 'Категория Syslog',
  'logs.failureCode': 'Примечание к разбору',
  'logs.structuredData': 'Структурированные данные',
  'logs.original': 'Исходное сообщение',
  'logs.originalHint':
    'Исходные байты показывают, что было получено. Поля и текст выше получены при разборе.',
  'logs.originalText': 'Просмотр текста',
  'logs.originalHex': 'Шестнадцатеричный просмотр',
  'logs.originalSize': 'Размер в байтах: {count}',
  'logs.originalEmpty': 'Исходное сообщение пустое',
  'logs.invalidBase64':
    'Не удалось отобразить исходное сообщение из-за некорректной кодировки.',
  'logs.rawTooLarge':
    'Размер исходного сообщения превышает предел. Просмотр остановлен.',
  'logs.invalidUtf8':
    'В оригинале есть байты вне UTF-8. В тексте они заменены. Проверьте байты в шестнадцатеричном режиме.',
  'logs.textTruncated': 'Показаны первые {count} символов текста.',
  'logs.hexTruncated': 'Показаны первые {count} байт в шестнадцатеричном виде.',
  'logs.messageTruncated': 'В предпросмотре показаны первые {count} символов.',
  'logs.notProvided': 'Не указано',
  'logs.notApplicable': '—',
  'logs.time.explicit': 'Время и часовой пояс указаны в сообщении',
  'logs.time.inferred_year_timezone':
    'Год определён по часовому поясу источника',
  'logs.time.inferred_ambiguous':
    'Неоднозначность летнего времени разрешена по времени приёма',
  'logs.time.inferred_distant':
    'Расчётное время заметно отличается от времени приёма',
  'logs.time.unknown': 'Нет допустимого времени устройства',
  'logs.validation.range':
    'Укажите корректное время начала и окончания. Окончание должно быть позже начала.',
  'logs.validation.maxRange': 'Период запроса не может превышать 30 дней.',
  'logs.validation.keywordsLength':
    'Ключевые слова не должны превышать 512 символов.',
  'logs.validation.keywords':
    'Введите слова из букв ASCII или цифр. Запрос только из знаков недопустим.',
  'logs.validation.fieldLength':
    'Имя хоста и приложения не должно превышать 255 символов.',
  'logs.validation.pageSize':
    'Размер страницы должен быть целым числом от 1 до 500.',
  'logs.validation.severity': 'Выберите допустимый уровень.',
  'menu.overview': 'Обзор',
  'menu.logs': 'Поиск журналов',
  'menu.sources': 'Источники',
  'menu.retention': 'Настройки хранения',
  'overview.title': 'Обзор',
  'overview.description':
    'Просматривайте состояние приёма, разбора, буферизации и записи журналов.',
  'overview.refresh': 'Обновить',
  'overview.autoRefresh': 'Обновление в реальном времени',
  'overview.lastReceive': 'Последний приём журнала: {time}',
  'overview.refreshedAt': 'Обновление панели: {time}',
  'overview.rate.title': 'Скорость обработки журналов',
  'overview.rate.sourceFilter': 'Фильтр по источнику журналов',
  'overview.rate.allSources': 'Все',
  'overview.rate.received': 'Скорость приёма',
  'overview.rate.written': 'Скорость записи',
  'overview.rate.unit': 'сообщ./с',
  'overview.rate.window': 'Последние {seconds} с',
  'overview.rate.waiting': 'Ожидание данных в реальном времени',
  'overview.rate.latest': 'Последние значения',
  'overview.rate.sampleTime': 'Время замера',
  'overview.rate.noSample': 'Нет данных',
  'overview.rate.ignored': 'Скорость фильтрации',
  'overview.rate.chartLabel':
    'График скорости приёма, записи и фильтрации журналов в реальном времени',
  'overview.live.connecting': 'Подключение',
  'overview.live.connected': 'Соединение активно',
  'overview.live.reconnecting': 'Повторное подключение',
  'overview.live.disconnected': 'Соединение прервано',
  'overview.live.paused': 'Обновление приостановлено',
  'overview.live.disconnectedHint':
    'Соединение прервано, отображаемые данные больше не обновляются. Подключитесь повторно.',
  'overview.live.waitingHint': 'Ожидание актуальных данных от сервера.',
  'overview.live.pausedHint':
    'Показаны данные до приостановки. Включите обновление в реальном времени или нажмите «Обновить», чтобы получить актуальное состояние.',
  'overview.received': 'Получено',
  'overview.receivedHint': 'Сообщения, полученные приложением · {bytes}',
  'overview.written': 'Записано',
  'overview.writtenHint': 'Запись в хранилище подтверждена',
  'overview.dropped': 'Отброшено',
  'overview.droppedHint':
    'Сброшено из-за ограничения скорости, превышения лимитов или переполнения очереди',
  'overview.sources': 'Зарегистрированные источники',
  'overview.sourcesHint':
    'Включено: {enabled} · Настройка не показывает, подключено ли устройство',
  'overview.pipeline': 'Приём и разбор',
  'overview.listener': 'Приёмник',
  'overview.ingest': 'Обработка',
  'overview.parsed': 'Разобрано успешно',
  'overview.parseFailed': 'Ошибки разбора',
  'overview.parseUnknown': 'Неизвестный формат',
  'overview.lastWrite': 'Последняя успешная запись (время Шанхая)',
  'overview.noWrite': 'Успешных записей пока нет',
  'overview.buffer': 'Буферизация и запись',
  'overview.queueBytes': 'Размер очереди приёма',
  'overview.spoolBytes': 'Текущий объём локального буфера',
  'overview.pendingBatches': 'Пакеты, ожидающие записи сейчас',
  'overview.databaseSize': 'Текущий размер базы журналов',
  'overview.writeFailures': 'Ошибки записи',
  'overview.bufferHint':
    'Чтобы заметить накопление очереди, следите, продолжают ли расти число ожидающих пакетов и объём локального буфера.',
  'overview.counterHint':
    'Счётчики ведутся с запуска процесса и сбрасываются при перезапуске. Потери до приёма операционной системой не учитываются.',
  'overview.statusError': 'Состояние работы недоступно',
  'overview.sourceError': 'Сведения об источниках недоступны',
  'overview.emptyTitle': 'Источники журналов не зарегистрированы',
  'overview.emptyDescription':
    'Добавьте адрес устройства или приложения, затем настройте пересылку журналов.',
  'overview.manageSources': 'Управление источниками',
  'overview.runtimeIssue': 'Проверьте обработку',
  'overview.retryTitle': 'Запись журналов временно затруднена',
  'overview.retryHint':
    'Пакеты остаются в локальном буфере. Система автоматически повторит запись.',
  'overview.listenerIssue': 'Проверьте подключение приёмника',
  'overview.state.running': 'Работает',
  'overview.state.listening': 'Прослушивает',
  'overview.state.starting': 'Запускается',
  'overview.state.disabled': 'Отключено',
  'overview.state.stopped': 'Остановлено',
  'overview.state.failed': 'Сбой',
  'overview.state.retrying': 'Автоматический повтор',
  'overview.state.writePaused': 'Запись приостановлена',
  'overview.state.unknown': '—',
  'overview.issue.writePaused':
    'Запись приостановлена, поскольку результат записи части пакетов не подтверждён.',
  'overview.issue.storageConnection':
    'Не удалось подключиться к ClickHouse. Проверьте службу и параметры подключения.',
  'overview.issue.storageAccess':
    'ClickHouse отклонил запись. Проверьте учётную запись и права записи.',
  'overview.issue.storageTimeout':
    'Истекло время ожидания записи в ClickHouse или проверки хранилища.',
  'overview.issue.storageUnavailable':
    'Хранилище журналов не завершило запрос на запись.',
  'overview.issue.clearPending':
    'Запись приостановлена, поскольку результат последней очистки журналов не подтверждён. Пакеты остаются в очереди.',
  'overview.issue.spoolFull':
    'Локальный буфер заполнен. Система ждёт освобождения места. При длительной задержке сообщения могут отбрасываться.',
  'overview.issue.contract':
    'Обработка остановлена после ошибки проверки записи. Исходные пакеты сохранены для исправления.',
  'overview.issue.recovery':
    'Обработка остановлена. Данные буфера требуют восстановления администратором.',
  'overview.issue.capacity':
    'Обработка остановлена. Размер пакета превышает ёмкость буфера. Проверьте пакет и настройки ёмкости.',
  'overview.issue.sources':
    'Конфигурация источников недоступна. Приём новых сообщений приостановлен до повторной проверки.',
  'overview.issue.listenerConfig':
    'Ошибка проверки настроек приёма. Проверьте адрес, протоколы и лимиты ресурсов.',
  'overview.issue.tls':
    'Не удалось запустить защищённый приём. Проверьте сертификат и настройки TLS.',
  'overview.issue.listener':
    'Ошибка приёмника. Проверьте адрес прослушивания и состояние сети.',
  'overview.issue.connection':
    'Недавно произошёл сбой подключения или обработки сообщений. Проверьте отправку у источника и состояние приёма.',
  'source.title': 'Источники журналов',
  'source.description':
    'Регистрируйте устройства и настраивайте приём, интерпретацию времени и срок хранения журналов.',
  'source.add': 'Добавить источник',
  'source.edit': 'Изменить источник',
  'source.refresh': 'Обновить',
  'source.search': 'Поиск по имени, IP-адресу или часовому поясу',
  'source.all': 'Все настройки приёма',
  'source.enabled': 'Приём включён',
  'source.disabled': 'Приём отключён',
  'source.count': 'Всего: {total} · По фильтру: {count}',
  'source.countUnknown': 'Число источников —',
  'source.name': 'Имя источника',
  'source.namePlaceholder': 'Например, магистральный коммутатор',
  'source.address': 'IP источника',
  'source.addressPlaceholder': 'Например, 192.0.2.10',
  'source.timezone': 'Часовой пояс источника',
  'source.timezonePlaceholder': 'Выберите или введите часовой пояс IANA',
  'source.timezoneHint':
    'Используется для времени устройства без указанного часового пояса.',
  'source.retention': 'Срок хранения',
  'source.state': 'Настройки приёма',
  'source.ignoredSeverities': 'Игнорируемые уровни важности',
  'source.ignoreNone': 'Сохранять все уровни',
  'source.ignoreScope':
    'Правило действует для вновь полученных журналов. Записи без уровня важности обрабатываются в обычном порядке.',
  'source.ignoreConfirmTitle': 'Подтверждение настроек источника',
  'source.ignoreMigrationRequired':
    'Обновите управляющую базу данных перед сохранением фильтров по уровню важности.',
  'source.stateHint':
    'Включённый приём не показывает, подключено ли устройство. Изменения применяются после обновления настроек источника.',
  'source.enabledSwitch': 'Разрешить приём от этого источника',
  'source.actions': 'Действия',
  'source.save': 'Сохранить',
  'source.cancel': 'Отмена',
  'source.back': 'Вернуться к редактированию',
  'source.saveConfirmed': 'Подтвердить и сохранить',
  'source.empty': 'Источники журналов не зарегистрированы',
  'source.emptyHint':
    'Сначала добавьте IP, с которого устройство отправляет журналы, чтобы система распознала источник.',
  'source.noMatches': 'Нет источников, соответствующих фильтру',
  'source.loadFailed': 'Не удалось загрузить список источников',
  'source.loading': 'Загрузка источников',
  'source.unavailable': 'Источники временно недоступны',
  'source.retry': 'Повторить загрузку',
  'source.stale': 'Показаны данные последней успешной загрузки.',
  'source.required': 'Заполните поле',
  'source.nameMax': 'Имя источника не должно превышать 128 символов',
  'source.addressInvalid':
    'Введите IPv4- или IPv6-адрес. Адрес проверяется сервером.',
  'source.zoneMax': 'Имя часового пояса не должно превышать 128 символов',
  'source.saveFailed':
    'Сохранить не удалось. Введённые данные остались в форме',
  'source.sourceChanged':
    'Настройки источника изменились. Откройте окно редактирования заново.',
  'retention.title': 'Хранение журналов',
  'retention.description':
    'Задайте срок хранения новых журналов по источникам. Обычно это от одного до трёх лет.',
  'retention.boundary':
    'Просроченные журналы скрываются из результатов поиска. Физическое удаление пока не реализовано, поэтому место не освобождается.',
  'retention.edit': 'Изменить срок хранения',
  'retention.days': '{days} дн.',
  'retention.unit': 'дн.',
  'retention.year1': '1 год · 365 дней',
  'retention.year2': '2 года · 730 дней',
  'retention.year3': '3 года · 1095 дней',
  'retention.range': 'Введите целое число дней от 1 до 3650',
  'retention.hint':
    'Срок задаётся в днях. Изменение относится к новым журналам.',
  'retention.confirmTitle': 'Подтверждение срока хранения',
  'retention.confirmDescription':
    'Срок для источника «{name}» изменится с {before} на {after} дней.',
  'retention.confirmScope':
    'Новый срок действует для журналов, полученных после изменения. Срок существующих записей не меняется, и они не удаляются.',
  'retention.source': 'Текущий источник',
  'retention.empty': 'Нет источников для настройки срока хранения',
  'retention.emptyHint':
    'Сначала зарегистрируйте устройство в разделе «Источники».',
} satisfies Record<keyof typeof source, string>;
