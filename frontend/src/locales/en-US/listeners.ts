export default {
  'listeners.legacyConfiguration':
    'The legacy server configuration has multiple listeners for the same protocol. Configure one listener per protocol on the server and restart the service. The current runtime configuration is unchanged.',
  'listeners.failure.io':
    'Binding or network I/O failed. Check port conflicts, address and permissions.',
  'listeners.failure.connectionIo':
    'The last client connection had a read or write error. The port still accepts new connections.',
  'listeners.failure.tls':
    'TLS failed to start. Check the server certificate, private key and validity dates.',
  'listeners.failure.sources':
    'Log source configuration is unavailable. Reception is paused.',
  'listeners.failure.configuration':
    'Startup settings could not be loaded. Check the control database and configuration version.',
  'listeners.failure.forcedDisabled':
    'Reception is forcibly disabled by the server. Check startup settings.',
  'menu.listeners': 'Receiver settings',
  'listeners.title': 'Receiver settings',
  'listeners.description': 'Configure UDP, TCP, TLS, and RELP log reception.',
  'listeners.reload': 'Reload settings',
  'listeners.restartHint':
    'After saving listener settings, turn the protocol or the main reception switch off and on to apply them.',
  'listeners.boundary':
    'Turning off the main switch stops all listeners and closes their connections. You can still save which protocols to enable.',
  'listeners.loadFailed': 'Could not load receiver settings',
  'listeners.saved': 'Settings saved',
  'listeners.running': 'Current runtime configuration',
  'listeners.pending': 'Settings pending',
  'listeners.current': 'Configuration loaded',
  'listeners.master': 'Receive logs',
  'listeners.masterHint':
    'While the main switch is on, toggling a protocol affects only that protocol.',
  'listeners.protocol': 'Protocol',
  'listeners.address': 'Bind address',
  'listeners.port': 'Port',
  'listeners.state': 'Listener status',
  'listeners.failure': 'Issue',
  'listeners.listenerFailed':
    'Receiver issue. Check bind address, port and certificate settings.',
  'listeners.savedConfiguration': 'Saved receiver configuration',
  'listeners.enabled': 'Enable protocol',
  'listeners.addressRequired': 'Enter an IPv4 or IPv6 bind address.',
  'listeners.portRequired': 'Port must be an integer from 1 to 65535.',
  'listeners.framing': 'Message framing',
  'listeners.connectionErrors': 'Client connection errors',
  'listeners.clientAddress': 'Client IP',
  'listeners.errorOccurredAt': 'Occurred at',
  'listeners.auto': 'Auto detect (octet count / newline)',
  'listeners.octet': 'Octet counting',
  'listeners.newline': 'Newline (LF / CRLF)',
  'listeners.certificate': 'TLS certificate',
  'listeners.certificateRequired':
    'Select an available server certificate before enabling TLS.',
  'listeners.noCertificates':
    'No server certificates are configured. Configure a certificate on the server and restart the service. This page does not collect private keys or passwords.',
  'listeners.frameTimeout': 'Frame timeout (seconds)',
  'listeners.idleTimeout': 'Idle timeout (seconds)',
  'listeners.timeoutRequired':
    'Enter an integer number of seconds within the allowed range.',
  'listeners.relpHint':
    'A RELP acknowledgement means the receiver accepted the event for processing, not that it was stored in the log database. Unregistered devices retain only their latest 100 in-memory samples, cleared on restart.',
  'listeners.save': 'Save receiver settings',
  'listeners.confirmTitle': 'Confirm receiver settings',
  'listeners.confirmSave': 'Confirm save',
  'listeners.confirmHint':
    'Check each device’s protocol, destination address, and port. After saving, turn the protocol or the main switch off and on to apply the settings.',
  'listeners.saveFailed': 'Could not confirm the save result',
  'listeners.conflict':
    'Another administrator changed these settings. Cancel and reload before editing again.',
  'listeners.migrationRequired':
    'The control database requires an explicit listener settings migration. Ask an administrator to migrate it and retry.',
  'listeners.invalid':
    'Invalid receiver settings. Check bind addresses, ports and timeout ranges.',
  'listeners.portConflict':
    'Enabled listeners have conflicting addresses or ports. Adjust them before saving.',
  'listeners.state.disabled': 'Disabled',
  'listeners.state.starting': 'Starting',
  'listeners.state.listening': 'Listening',
  'listeners.state.failed': 'Start or receive failure',
  'listeners.state.stopped': 'Stopped',
  'listeners.state.unknown': 'Unknown',
  'listeners.confirmEnable': 'Turn on log reception?',
  'listeners.confirmDisable': 'Turn off log reception?',
  'listeners.confirmSwitch': 'Confirm switch',
  'listeners.enableHint':
    'The latest saved settings will be loaded and the enabled protocol ports started.',
  'listeners.disableHint':
    'Receiver ports and existing reception connections will close.',
  'listeners.switchFailed':
    'Could not confirm the reception state. Reload the settings.',
  'listeners.protocolEnable': 'Enable protocol?',
  'listeners.protocolDisable': 'Disable protocol?',
  'listeners.protocolEnableHint':
    'This listener will start with its latest saved settings.',
  'listeners.protocolDisableHint':
    'This listener will stop and close its existing connections.',
  'listeners.protocolSavedHint':
    'The main switch is off. This only saves whether the protocol is enabled.',
  'listeners.streamInterrupted':
    'The live connection was interrupted. Showing the last received status.',
};
