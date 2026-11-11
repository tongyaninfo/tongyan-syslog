import type source from '../zh-CN/liveLogs';

export default {
  'live.cacheId': 'Temporary cache ID',
  'menu.liveLogs': 'Live logs',
  'live.title': 'Live logs',
  'live.subtitle':
    'Inspect recently received device logs, including ignored logs. The memory cache is cleared on restart.',
  'live.settings': 'Cache settings',
  'live.settingsHint':
    'Changes apply immediately. Defaults: 5,000 records per device and 512 MiB total. Old records are evicted at either limit.',
  'live.perDeviceLimit': 'Records per device',
  'live.memoryLimit': 'Total memory budget',
  'live.limitValidation': 'Enter an integer from 100 to 50,000.',
  'live.memoryValidation': 'Enter 16–2048 MiB.',
  'live.shrinkHint':
    'Reducing limits immediately evicts older cached records. Evicted cache entries cannot be restored.',
  'live.entries': 'Cached records',
  'live.devices': 'Devices',
  'live.memory': 'Cache memory',
  'live.budget': 'Budget',
  'live.cacheHint': 'Retained separately per registered source',
  'live.evicted': 'Evicted records',
  'live.rejected': 'Rejected records',
  'live.disposition': 'Disposition',
  'live.disposition.all': 'All',
  'live.disposition.normal': 'Received',
  'live.disposition.ignored': 'Ignored · not stored',
  'live.time.live': 'All cached times · live',
  'live.time.fixed': 'Fixed time range',
  'live.apply': 'Apply filters',
  'live.dirty': 'Filters changed. Apply to subscribe.',
  'live.stream': 'Live stream',
  'live.connection.connecting': 'Connecting',
  'live.connection.connected': 'Live connection active',
  'live.connection.reconnecting': 'Reconnecting',
  'live.connection.disconnected': 'Disconnected',
  'live.connection.paused': 'Paused',
  'live.resume': 'Resume live',
  'live.pause': 'Pause',
  'live.refresh': 'Reload cache',
  'live.clear': 'Clear display',
  'live.evidenceHint':
    'Live records do not prove database writes. Ignored logs are not stored. Clear affects this display only. Scrolling down pauses updates to preserve your reading position.',
  'live.pausedHint':
    'Display frozen and this browser unsubscribed. The server continues caching. Resume attempts to catch up.',
  'live.gap':
    'Cache reset or records evicted. Recent logs were reloaded; missing records cannot be recovered from the live cache.',
  'live.budgetLimited':
    'This batch reached its record or capacity budget. Only retrieved matches are shown; load earlier records or wait for further updates.',
  'live.spoolFull': 'Disk spool is full; received logs may be dropped.',
  'live.empty': 'No matching cached logs. Waiting for new records.',
  'live.displayed': 'Displayed records',
  'live.heartbeat': 'Last connection heartbeat',
  'live.queued': 'Queued',
  'live.dropped': 'Dropped',
  'live.loadOlder': 'Load 500 earlier records',
  'live.limitHint':
    'Initially loads 500 recent records. This display retains at most 5,000 summaries, evicting the oldest as new records arrive. Details are loaded from memory on demand.',
  'live.truncated': 'Truncated summary',
  'live.normalHint':
    'Received: this record is from the memory cache and does not confirm a ClickHouse write.',
  'live.ignoredHint':
    'Ignored by source severity rules. Kept only as a live memory sample, never written to the database.',
  'live.error.migration':
    'Upgrade the control database explicitly before using live cache settings.',
  'live.error.cacheReset': 'Cache reset. Reload recent records.',
  'live.error.unavailable':
    'Live cache unavailable. Check service configuration and retry.',
  'live.error.busy': 'Live query busy. Retry shortly.',
  'live.error.evicted':
    'This entry was evicted or the cache restarted. Reload the list.',
  'live.error.conflict':
    'Settings changed elsewhere. Close and reopen settings. Your input is preserved.',
} satisfies Record<keyof typeof source, string>;
