import httpsCertificate from './en-US/httpsCertificate';
import listeners from './en-US/listeners';
import liveLogs from './en-US/liveLogs';
import mcp from './en-US/mcp';
import notifications from './en-US/notifications';
import unregistered from './en-US/unregistered';
import users from './en-US/users';
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
  'app.description': 'Centralized log management platform',
  'app.logout': 'Sign out',
  'app.logoutFailed':
    'Could not sign out. Your session is still active. Try again.',
  'app.theme': 'Appearance mode',
  'app.light': 'Light',
  'app.dark': 'Dark',
  'app.auto': 'Auto',
  'app.account': 'Administrator',
  'app.preview': 'Synthetic data preview · UI verification',
  'app.sessionLoading': 'Verifying session…',
  'app.retry': 'Reload',
  'app.notFound': 'Page not found',
  'app.back': 'Back to overview',
  'app.timezone': 'Beijing time · UTC+8',
  'app.appearance': 'Theme settings',
  'app.appearanceStyle': 'Appearance',
  'app.autoHint': 'Auto follows your operating system’s light or dark setting.',
  'app.language': 'System language',
  'app.userMenu': 'Account menu',
  'app.changePassword': 'Change password',
  'app.currentPassword': 'Current password',
  'app.newPassword': 'New password',
  'app.confirmPassword': 'Confirm new password',
  'app.passwordRequired': 'Enter a password.',
  'app.passwordLength': 'Use 8 to 1024 characters for the new password.',
  'app.passwordMismatch': 'The new passwords do not match.',
  'app.passwordChanged': 'Password changed. Sign in again.',
  'app.passwordHint': 'Changing your password signs you out of all sessions.',
  'app.passwordPreviewHint':
    'The preview demonstrates the form and sign-in flow. The sample password stays the same.',
  'app.passwordPreviewChanged':
    'Preview complete. Sign in again with the original preview password.',
  'app.cancel': 'Cancel',
  'app.save': 'Save',
  'login.welcome': 'Welcome back',
  'login.subtitle': 'Sign in to view device logs and reception status.',
  'login.hero': 'Trace every log event',
  'login.description':
    'Collect, search and trace device and application logs from one console.',
  'login.collect': 'Centralized collection',
  'login.collectDescription':
    'Collect Syslog from switches, ESXi and applications',
  'login.search': 'Fast search',
  'login.searchDescription':
    'Find events by time, source, severity and keyword',
  'login.evidence': 'Original evidence',
  'login.evidenceDescription':
    'Compare structured fields with the original message',
  'login.username': 'Username',
  'login.password': 'Password',
  'login.usernameRequired': 'Enter your username',
  'login.passwordRequired': 'Enter your password',
  'login.submit': 'Sign in',
  'login.sessionHint':
    'The console uses a secure session and does not store your password in the browser.',
  'login.initialAccount':
    'Your system administrator creates the account in advance.',
  'parse.parsed': 'Parsed',
  'parse.failed': 'Parse failed',
  'parse.unknown': 'Unrecognized format',
  'severity.0': 'Emergency',
  'severity.1': 'Alert',
  'severity.2': 'Critical',
  'severity.3': 'Error',
  'severity.4': 'Warning',
  'severity.5': 'Notice',
  'severity.6': 'Informational',
  'severity.7': 'Debug',
  'error.generic': 'Service unavailable. Try again later.',
  'error.auth': 'Incorrect username or password.',
  'error.session': 'Your session has expired. Sign in again.',
  'error.input': 'Check your input and try again.',
  'error.query': 'Check the time range, page size and filters.',
  'error.token':
    'Keywords match whole ASCII letter or digit tokens and are case-sensitive.',
  'error.permission': 'Your account cannot perform this action.',
  'error.notFound': 'The record is missing, expired or inaccessible.',
  'error.conflict':
    'This source IP is already registered. Check for duplicates.',
  'error.csrf':
    'Request origin verification failed. Sign in again from the original console address.',
  'error.https':
    'Sign-in requires a secure connection. Open the console over HTTPS.',
  'error.rate': 'Too many requests. Try again later.',
  'error.timeout':
    'The request timed out. Try again or narrow the search range.',
  'error.reconcile':
    'Storage records need reconciliation. Contact your administrator.',
  'error.network':
    'Could not reach the service. Check your connection and try again.',
  'error.currentPassword': 'Incorrect current password.',
  'error.passwordInvalid': 'The new password does not meet the requirements.',
  'error.passwordUnchanged':
    'Choose a password different from your current one.',
  'error.tooManyRequests': 'Too many requests. Try again later.',
  'logs.title': 'Log search',
  'logs.subtitle':
    'Find logs by time and source, then inspect parsed fields and the original message.',
  'logs.range': 'Received time',
  'logs.timezone': 'Times shown in Asia/Shanghai',
  'logs.rangeHint':
    'Search up to 30 days at a time within the retention period.',
  'logs.lastHour': 'Last hour',
  'logs.lastDay': 'Last 24 hours',
  'logs.lastWeek': 'Last 7 days',
  'logs.lastMonth': 'Last 30 days',
  'logs.source': 'Log source',
  'logs.allSources': 'All registered sources',
  'logs.severity': 'Severity',
  'logs.allSeverities': 'All severities',
  'logs.hostname': 'Hostname',
  'logs.hostnamePlaceholder': 'Exact hostname from the message',
  'logs.appName': 'Application',
  'logs.appNamePlaceholder': 'Exact application name',
  'logs.keywords': 'Message keywords',
  'logs.keywordsPlaceholder': 'For example, LINK DOWN',
  'logs.keywordHint':
    'Matches whole ASCII alphanumeric tokens with case sensitivity. Every token must occur. Non-ASCII text and substrings are unsupported.',
  'logs.pageSize': 'Rows per page',
  'logs.pageSizeValue': '{count} rows',
  'logs.search': 'Search logs',
  'logs.clearAction': 'Clear logs',
  'logs.clearTitle': 'Clear all stored logs',
  'logs.clearSourceTitle': 'Clear logs for this device',
  'logs.clearSourceScope':
    'Stored log history for {name} ({address}) will be cleared.',
  'logs.clearSourceSucceeded': 'Cleared log history for {name}.',
  'logs.clearSubmit': 'Confirm clear',
  'logs.clearScope':
    'This permanently clears every Syslog record stored in ClickHouse, including records outside the current filters.',
  'logs.clearPendingLogs':
    'Log reception and storage continue after clearing. Pending records may appear later.',
  'logs.clearConfirmation': 'Type CLEAR to confirm',
  'logs.clearSucceeded': 'Logs cleared.',
  'logs.clearInProgress':
    'Another clear request is in progress. Wait for it to finish and refresh your search.',
  'logs.clearSizeLimit':
    'The log table exceeds the size limit for clearing. Ask an administrator to review the ClickHouse settings.',
  'logs.clearOutcomeUnknown':
    'The clear result could not be confirmed. Search again to verify the result before submitting another request.',
  'logs.clearStorageAccess':
    'The log store refused the clear request. Ask an administrator to verify the storage account permissions.',
  'logs.clearFailed':
    'The clear request did not complete. Check the service status.',
  'logs.reset': 'Reset',
  'logs.refresh': 'Run again',
  'logs.results': 'Search results',
  'logs.resultsHint':
    'Matching logs are ordered by receipt time, newest first.',
  'logs.page': 'Page {page}',
  'logs.rows': '{count} rows on this page',
  'logs.previous': 'Previous',
  'logs.next': 'Next',
  'logs.noMore': 'End of results',
  'logs.empty': 'No matching logs',
  'logs.emptyHint': 'Adjust the time range, source or keywords and try again.',
  'logs.pending': 'Filters changed',
  'logs.pendingHint': 'Select Search logs to apply the current filters.',
  'logs.loading': 'Searching logs',
  'logs.queryFailed': 'Log search did not complete',
  'logs.pageFailed': 'Could not load page {page}',
  'logs.restartFromFirstPage': 'Search from the first page',
  'logs.error.storageConnection':
    'Could not connect to log storage. Check that ClickHouse is running and the connection settings are correct, then retry.',
  'logs.error.storageAccess':
    'Log storage denied access. Ask an administrator to check the storage account and read permissions.',
  'logs.error.storageSchema':
    'A required log database or table is missing. Ask an administrator to check storage initialization.',
  'logs.error.storageSchemaIncompatible':
    'The existing ClickHouse log table is incompatible. Ask an administrator to verify its definition.',
  'logs.error.storageTimeout':
    'The log storage request timed out. Try a shorter time range.',
  'logs.error.storageUnavailable':
    'Log storage could not complete the request. Check its status, then retry.',
  'logs.error.invalidResponse':
    'The log service returned an unreadable response. Retry, or contact an administrator if the problem persists.',
  'logs.error.queryUnavailable':
    'Log search is temporarily unavailable. Retry with the current filters, or contact an administrator if the problem persists.',
  'logs.sourcesFailed': 'Source list unavailable',
  'logs.sourcesRetry': 'Reload sources',
  'logs.noSources': 'No registered log sources',
  'logs.noSourcesHint':
    'Register the device address in Sources before configuring it to send logs.',
  'logs.receivedAt': 'Received at',
  'logs.eventTime': 'Device time',
  'logs.message': 'Message',
  'logs.parseStatus': 'Parse status',
  'logs.parserVersion': 'Parser version',
  'logs.action': 'Action',
  'logs.detail': 'View details',
  'logs.detailTitle': 'Log details',
  'logs.detailLoading': 'Loading log details',
  'logs.detailFailed': 'Could not load the log',
  'logs.close': 'Close',
  'logs.retry': 'Retry',
  'logs.eventId': 'Event ID',
  'logs.sourceId': 'Source ID',
  'logs.peerAddress': 'Peer address',
  'logs.transport': 'Transport',
  'logs.expiresAt': 'Retain until',
  'logs.timeConfidence': 'Time interpretation',
  'logs.originalTimestamp': 'Original timestamp',
  'logs.procId': 'Process ID',
  'logs.msgId': 'Message ID',
  'logs.facility': 'Facility',
  'logs.failureCode': 'Parse diagnostic',
  'logs.structuredData': 'Structured data',
  'logs.original': 'Original message',
  'logs.originalHint':
    'The raw bytes record what was received. The fields and text above are parsed results.',
  'logs.originalText': 'Text preview',
  'logs.originalHex': 'Hex preview',
  'logs.originalSize': 'Original size: {count} bytes',
  'logs.originalEmpty': 'The original message is empty',
  'logs.invalidBase64':
    'The original message encoding is invalid. A preview is unavailable.',
  'logs.rawTooLarge':
    'The original message exceeds the byte limit. Preview stopped.',
  'logs.invalidUtf8':
    'The original contains invalid UTF-8 bytes. Text shows replacement characters. Check the hex preview for the original bytes.',
  'logs.textTruncated': 'Text preview shows only the first {count} characters.',
  'logs.hexTruncated': 'Hex preview shows only the first {count} bytes.',
  'logs.messageTruncated': 'Preview shows only the first {count} characters.',
  'logs.notProvided': 'Not provided',
  'logs.notApplicable': '—',
  'logs.time.explicit': 'Time and zone are explicit in the message',
  'logs.time.inferred_year_timezone': 'Year inferred from the source time zone',
  'logs.time.inferred_ambiguous':
    'Daylight saving ambiguity resolved using reception time',
  'logs.time.inferred_distant': 'Inferred time is far from the reception time',
  'logs.time.unknown': 'No valid device time',
  'logs.validation.range':
    'Choose valid start and end times. The end must follow the start.',
  'logs.validation.maxRange': 'The time range cannot exceed 30 days.',
  'logs.validation.keywordsLength': 'Keywords cannot exceed 512 characters.',
  'logs.validation.keywords':
    'Enter whole ASCII letter or digit tokens. Symbols alone are not valid.',
  'logs.validation.fieldLength':
    'Hostname and application name cannot exceed 255 characters.',
  'logs.validation.pageSize': 'Rows per page must be an integer from 1 to 500.',
  'logs.validation.severity': 'Choose a valid severity.',
  'menu.overview': 'Overview',
  'menu.logs': 'Log search',
  'menu.sources': 'Log sources',
  'menu.retention': 'Retention settings',
  'overview.title': 'Overview',
  'overview.description':
    'View the current status of log reception, parsing, buffering and writes.',
  'overview.refresh': 'Refresh',
  'overview.autoRefresh': 'Live updates',
  'overview.lastReceive': 'Last log received: {time}',
  'overview.refreshedAt': 'Dashboard updated: {time}',
  'overview.rate.title': 'Log rates',
  'overview.rate.sourceFilter': 'Filter by log source',
  'overview.rate.allSources': 'All',
  'overview.rate.received': 'Receive rate',
  'overview.rate.written': 'Write rate',
  'overview.rate.unit': 'logs/s',
  'overview.rate.window': 'Last {seconds} seconds',
  'overview.rate.waiting': 'Waiting for live samples',
  'overview.rate.latest': 'Latest rates',
  'overview.rate.sampleTime': 'Sample time',
  'overview.rate.noSample': 'No sample',
  'overview.rate.ignored': 'Ignore rate',
  'overview.rate.chartLabel':
    'Live chart of log receive, write, and ignore rates',
  'overview.live.connecting': 'Connecting',
  'overview.live.connected': 'Live connection active',
  'overview.live.reconnecting': 'Reconnecting',
  'overview.live.disconnected': 'Live connection disconnected',
  'overview.live.paused': 'Live updates paused',
  'overview.live.disconnectedHint':
    'The connection was interrupted. Displayed data is no longer updating. Please reconnect.',
  'overview.live.waitingHint': 'Waiting for the latest data from the server.',
  'overview.live.pausedHint':
    'Data from before the pause is shown. Enable live updates or select Refresh to get the latest status.',
  'overview.received': 'Received',
  'overview.receivedHint': 'Messages observed by the application · {bytes}',
  'overview.written': 'Written',
  'overview.writtenHint': 'Confirmed writes to log storage',
  'overview.dropped': 'Dropped',
  'overview.droppedHint':
    'Dropped due to rate limits, size limits or a full queue',
  'overview.sources': 'Registered sources',
  'overview.sourcesHint':
    'Enabled: {enabled} · Configuration does not show whether a device is online',
  'overview.pipeline': 'Reception and parsing',
  'overview.listener': 'Listener',
  'overview.ingest': 'Processing',
  'overview.parsed': 'Parsed',
  'overview.parseFailed': 'Parse failures',
  'overview.parseUnknown': 'Unknown format',
  'overview.lastWrite': 'Last successful write (Shanghai time)',
  'overview.noWrite': 'No successful writes yet',
  'overview.buffer': 'Buffering and writes',
  'overview.queueBytes': 'Receive queue usage',
  'overview.spoolBytes': 'Current local spool usage',
  'overview.pendingBatches': 'Batches currently awaiting write',
  'overview.databaseSize': 'Current log database size',
  'overview.writeFailures': 'Write failures',
  'overview.bufferHint':
    'To spot a write backlog, check whether pending batches and local spool usage keep rising.',
  'overview.counterHint':
    'Cumulative counters reset when the process restarts. Loss before the operating system receives a packet is excluded.',
  'overview.statusError': 'Runtime status unavailable',
  'overview.sourceError': 'Source information unavailable',
  'overview.emptyTitle': 'No registered log sources',
  'overview.emptyDescription':
    'Add the source address of a device or application, then configure it to forward logs.',
  'overview.manageSources': 'Manage sources',
  'overview.runtimeIssue': 'Check processing',
  'overview.retryTitle': 'Log writes are temporarily blocked',
  'overview.retryHint':
    'Batches remain in the local spool. The system will retry writes automatically.',
  'overview.listenerIssue': 'Check listener connections',
  'overview.state.running': 'Running',
  'overview.state.listening': 'Listening',
  'overview.state.starting': 'Starting',
  'overview.state.disabled': 'Disabled',
  'overview.state.stopped': 'Stopped',
  'overview.state.failed': 'Failed',
  'overview.state.retrying': 'Retrying automatically',
  'overview.state.writePaused': 'Writes paused',
  'overview.state.unknown': '—',
  'overview.issue.writePaused':
    'Writes are paused because some batch results are unconfirmed.',
  'overview.issue.storageConnection':
    'Could not connect to ClickHouse. Check the service and connection settings.',
  'overview.issue.storageAccess':
    'ClickHouse denied writes. Check the storage account and write permissions.',
  'overview.issue.storageTimeout':
    'A ClickHouse write or storage check timed out.',
  'overview.issue.storageUnavailable':
    'Log storage could not complete the write request.',
  'overview.issue.clearPending':
    'Writes are paused because the last log clear result is unconfirmed. Batches remain queued.',
  'overview.issue.spoolFull':
    'The local spool is full and waiting for space. A sustained backlog can cause dropped messages.',
  'overview.issue.contract':
    'Processing stopped after batch write validation failed. Original batches are retained for recovery.',
  'overview.issue.recovery':
    'Processing stopped because buffered data needs recovery. Ask an administrator to check recovery status.',
  'overview.issue.capacity':
    'Processing stopped because a batch exceeded the spool capacity. Check the batch and capacity settings.',
  'overview.issue.sources':
    'Source configuration is unavailable. New messages are paused pending a configuration recheck.',
  'overview.issue.listenerConfig':
    'Listener configuration is invalid. Check addresses, protocols and resource limits.',
  'overview.issue.tls':
    'Encrypted reception could not start. Check the certificate and TLS listener settings.',
  'overview.issue.listener':
    'The listener encountered an error. Check the listener address and network status.',
  'overview.issue.connection':
    'A connection or message processing error occurred recently. Check source forwarding settings and reception status.',
  'source.title': 'Log sources',
  'source.description':
    'Register devices and configure reception, time interpretation and log retention.',
  'source.add': 'Add source',
  'source.edit': 'Edit source',
  'source.refresh': 'Refresh',
  'source.search': 'Search name, IP address or time zone',
  'source.all': 'All reception settings',
  'source.enabled': 'Reception enabled',
  'source.disabled': 'Reception disabled',
  'source.count': '{total} sources · {count} match the filters',
  'source.countUnknown': 'Source count —',
  'source.name': 'Source name',
  'source.namePlaceholder': 'For example, Core switch',
  'source.address': 'Source IP',
  'source.addressPlaceholder': 'For example, 192.0.2.10',
  'source.timezone': 'Source time zone',
  'source.timezonePlaceholder': 'Select or enter an IANA time zone',
  'source.timezoneHint':
    'Used to interpret device timestamps without a time zone.',
  'source.retention': 'Retention period',
  'source.state': 'Reception',
  'source.ignoredSeverities': 'Ignored severities',
  'source.ignoreNone': 'Keep all severities',
  'source.ignoreScope':
    'This rule applies to newly received logs. Logs without a severity are processed normally.',
  'source.ignoreConfirmTitle': 'Confirm source settings',
  'source.ignoreMigrationRequired':
    'Upgrade the control database before saving severity filters.',
  'source.stateHint':
    'Enabling reception does not show whether a device is online. Changes take effect after the source configuration refreshes.',
  'source.enabledSwitch': 'Allow reception from this source',
  'source.actions': 'Actions',
  'source.save': 'Save',
  'source.cancel': 'Cancel',
  'source.back': 'Back to editing',
  'source.saveConfirmed': 'Confirm and save',
  'source.empty': 'No registered log sources',
  'source.emptyHint':
    'Add the device’s sending IP so the system can recognize the source and receive its logs.',
  'source.noMatches': 'No sources match the filters',
  'source.loadFailed': 'Could not load sources',
  'source.loading': 'Loading sources',
  'source.unavailable': 'Sources unavailable after a loading error',
  'source.retry': 'Retry loading',
  'source.stale': 'Showing the last successfully loaded data.',
  'source.required': 'Fill in this field',
  'source.nameMax': 'Source name cannot exceed 128 characters',
  'source.addressInvalid':
    'Enter an IPv4 or IPv6 address. The server validates the address.',
  'source.zoneMax': 'Time zone name cannot exceed 128 characters',
  'source.saveFailed': 'Could not save. Your input is still in the form',
  'source.sourceChanged':
    'The source configuration changed. Reopen the editor and try again.',
  'retention.title': 'Log retention',
  'retention.description':
    'Set how many days to retain new logs for each source. Typical periods are one to three years.',
  'retention.boundary':
    'Expired logs are hidden from search. Physical cleanup is not implemented, so expiration does not free disk space.',
  'retention.edit': 'Adjust retention',
  'retention.days': '{days} days',
  'retention.unit': 'days',
  'retention.year1': '1 year · 365 days',
  'retention.year2': '2 years · 730 days',
  'retention.year3': '3 years · 1095 days',
  'retention.range': 'Enter a whole number of days from 1 to 3650',
  'retention.hint':
    'Measured in days. Changes apply to logs received from now on.',
  'retention.confirmTitle': 'Confirm retention change',
  'retention.confirmDescription':
    'Retention for {name} will change from {before} to {after} days.',
  'retention.confirmScope':
    'The new period applies to future logs. Existing logs keep their expiration times and are not deleted by this action.',
  'retention.source': 'Current source',
  'retention.empty': 'No sources available to configure',
  'retention.emptyHint': 'Register a device in Log sources first.',
} satisfies Record<keyof typeof source, string>;
