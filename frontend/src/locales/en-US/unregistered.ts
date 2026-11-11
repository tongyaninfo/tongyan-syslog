import type source from '../zh-CN/unregistered';

export default {
  'menu.unregistered': 'Unregistered devices',
  'unregistered.title': 'Unregistered devices',
  'unregistered.description':
    'Inspect sender IPs without source configuration, review recent samples and register a source in one click.',
  'unregistered.boundary':
    'Keep up to the latest 100 samples per IP in memory. Restarting clears them. The oldest samples are evicted when memory or device limits are reached.',
  'unregistered.defaults':
    'Quick add uses the IP as the name, Asia/Shanghai time zone, 365-day retention and enabled reception. Edit these later in Log sources.',
  'unregistered.firstSeen': 'First received',
  'unregistered.lastSeen': 'Last received',
  'unregistered.received': 'Accepted for sampling',
  'unregistered.samples': 'Current samples',
  'unregistered.viewLogs': 'View logs',
  'unregistered.add': 'Quick add',
  'unregistered.added': 'Source {ip} registered',
  'unregistered.addedHint':
    'Normal reception starts after the source snapshot refreshes (10 seconds by default). Buffered history is not copied into stored logs.',
  'unregistered.addFailed': 'Could not add source',
  'unregistered.loadFailed': 'Could not load unregistered devices',
  'unregistered.state': 'Sampling setting',
  'unregistered.enabled': 'Enabled',
  'unregistered.disabled': 'Disabled',
  'unregistered.devices': 'Devices / limit',
  'unregistered.memory': 'Memory / limit',
  'unregistered.currentSamples': 'Current sample total',
  'unregistered.evicted': 'Samples evicted',
  'unregistered.rejected': 'Rejected',
  'unregistered.networks': 'Allowed networks',
  'unregistered.noNetworks': 'No allowed networks configured',
  'unregistered.counterHint':
    'These counters describe temporary samples, not database writes. Totals start at process launch. Registered but disabled sources are excluded. Enabling sampling does not enable a listener.',
  'unregistered.search': 'Search sender IP',
  'unregistered.count': '{count} devices match',
  'unregistered.unavailable': 'Sample data unavailable',
  'unregistered.empty': 'No unregistered devices',
  'unregistered.sampleExpired':
    'This sample was evicted or the device was registered. Refresh the log list.',
  'unregistered.byteCount': 'Raw size',
  'unregistered.preview': 'Raw preview',
  'unregistered.viewRaw': 'View original',
  'unregistered.logsTitle': '{ip} · Temporary logs',
  'unregistered.logsHint':
    'Up to the latest 100 samples, newest first. These are reception samples and have not been written to the log database.',
  'unregistered.previewHint':
    'The list shows a limited raw preview. View the original to check bytes, encoding and truncation notices.',
  'unregistered.logsFailed': 'Could not load temporary logs',
  'unregistered.noLogs': 'No samples available. Refresh to check.',
  'unregistered.helpStep1':
    'Open Unregistered devices to see unregistered sender IPs within the allowed networks. Registered but disabled sources are not rediscovered.',
  'unregistered.helpStep2':
    'Each IP keeps up to 100 recent samples in memory, with default global limits of 64 MiB and 1,000 IPs. The oldest samples are evicted at capacity; restart clears all samples. Open logs to inspect individual raw messages.',
  'unregistered.helpStep3':
    'Quick add creates an enabled source named after its IP, with Asia/Shanghai time zone and 365-day retention. Normal reception begins after the source snapshot refreshes (10 seconds by default). Samples are not backfilled. Edit settings later in Log sources.',
} satisfies Record<keyof typeof source, string>;
