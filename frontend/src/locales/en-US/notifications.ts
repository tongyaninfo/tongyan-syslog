export default {
  'menu.notifications': 'Notifications',
  'notifications.description':
    'Configure email and log report subscriptions, and view delivery history.',
  'notifications.smtp': 'Email service',
  'notifications.reports': 'Report subscriptions',
  'notifications.history': 'Delivery history',
  'notifications.host': 'SMTP server',
  'notifications.port': 'SMTP port',
  'notifications.useTls': 'Enable SSL/TLS',
  'notifications.security': 'Encryption mode',
  'notifications.securityHint':
    'SSL/TLS typically uses port 465, and STARTTLS uses port 587.',
  'notifications.unencrypted':
    'Unencrypted SMTP is restricted to unauthenticated internal relays.',
  'notifications.username': 'SMTP username',
  'notifications.password': 'SMTP authorization code',
  'notifications.passwordHint':
    'Use the code provided by your mail service. Leave blank for an unauthenticated relay.',
  'notifications.keepPassword':
    'A code is saved. Leave blank to keep it, or re-enter it when changing the connection or account.',
  'notifications.senderAddress': 'Sender email address',
  'notifications.senderName': 'Sender name',
  'notifications.subjectPrefix': 'Subject prefix',
  'notifications.subjectPrefixHint':
    'Applies to test emails and scheduled reports. Leave blank to omit the prefix.',
  'notifications.subjectPreview': 'Subject preview',
  'notifications.smtpSaved': 'Email settings saved.',
  'notifications.testMail': 'Test email',
  'notifications.testHint': 'Test emails use the saved email settings.',
  'notifications.recipient': 'Recipient email',
  'notifications.sendTest': 'Send test email',
  'notifications.testQueued': 'Test email queued.',
  'notifications.invalidAddress': 'Enter a valid email address.',
  'notifications.addReport': 'Add subscription',
  'notifications.editReport': 'Edit subscription',
  'notifications.edit': 'Edit',
  'notifications.reportName': 'Subscription name',
  'notifications.frequency': 'Frequency',
  'notifications.sendTime': 'Send time',
  'notifications.scheduleHint':
    'Schedules use Asia/Shanghai time. Weekly reports are sent on Mondays, monthly reports on the first.',
  'notifications.allSourcesHint':
    'Leave blank to include all registered sources.',
  'notifications.recipients': 'Recipients',
  'notifications.recipientsHint':
    'Press Enter or type a comma to add an address. Up to 20 recipients.',
  'notifications.reportState': 'Subscription status',
  'notifications.enabled': 'Enabled',
  'notifications.disabled': 'Disabled',
  'notifications.nextSend': 'Next send (UTC+8)',
  'notifications.sourceCount': '{count} sources',
  'notifications.noReports': 'No report subscriptions',
  'notifications.mailKind': 'Email type',
  'notifications.subject': 'Subject',
  'notifications.deliveryStatus': 'Delivery status',
  'notifications.createdAt': 'Created at',
  'notifications.updatedAt': 'Updated at',
  'notifications.attempts': 'Send attempts',
  'notifications.period': 'Report period',
  'notifications.failureCode': 'Failure code',
  'notifications.filter': 'Apply filters',
  'notifications.noDeliveries': 'No delivery records',
  'notifications.deliveryDetail': 'Delivery details',
  'notifications.htmlPreview': 'Email preview',
  'notifications.textPreview': 'Plain text',
  'notifications.noBody': 'No email body is available for preview.',
  'notifications.kind.test': 'Test email',
  'notifications.kind.daily': 'Daily report',
  'notifications.kind.weekly': 'Weekly report',
  'notifications.kind.monthly': 'Monthly report',
  'notifications.status.queued': 'Queued',
  'notifications.status.sending': 'Sending',
  'notifications.status.accepted': 'Accepted by SMTP',
  'notifications.status.failed': 'Failed',
  'notifications.status.unknown': 'Outcome unknown',
  'notifications.migrationRequired':
    'Upgrade the control database before using notifications.',
  'notifications.smtpNotConfigured': 'Save the email service settings first.',
  'notifications.passwordRequired':
    'Enter the SMTP authorization code for this connection.',
  'notifications.conflict': 'Settings have changed. Reload them before saving.',
  'notifications.queueFull': 'The email queue is full. Try again later.',
  'notifications.subscriptionLimit':
    'The report subscription limit has been reached.',
  'notifications.authenticationFailed':
    'SMTP authentication failed. Check the username and authorization code.',
  'notifications.providerRejected':
    'The SMTP server rejected the message. Check the sender, recipient and provider limits.',
  'notifications.tlsFailed':
    'Could not establish encryption. Check the port, encryption mode and server certificate.',
  'notifications.smtpConnectionFailed':
    'Could not connect to the SMTP server. Check the address, port and network.',
  'notifications.reportUnavailable':
    'Could not generate the report. Check log storage status.',
  'notifications.subscriptionChanged':
    'The subscription changed before the previous report was sent.',
};
