import type source from '../zh-CN/users';

export default {
  'menu.users': 'Users',
  'users.title': 'User management',
  'users.description': 'Manage console accounts, enabled status and passwords.',
  'users.permissionHint':
    'All current accounts are administrators who can view all logs and change system settings.',
  'users.search': 'Search usernames',
  'users.filter': 'Account status',
  'users.all': 'All statuses',
  'users.enabled': 'Enabled',
  'users.disabled': 'Disabled',
  'users.username': 'Username',
  'users.role': 'Role',
  'users.admin': 'Administrator',
  'users.createdAt': 'Created',
  'users.actions': 'Actions',
  'users.current': 'Current account',
  'users.add': 'Add user',
  'users.edit': 'Edit user',
  'users.enable': 'Enable',
  'users.disable': 'Disable',
  'users.resetPassword': 'Reset password',
  'users.refresh': 'Refresh',
  'users.total': '{total} accounts',
  'users.empty': 'No matching accounts',
  'users.unavailable': 'Account list is temporarily unavailable',
  'users.loadFailed': 'Could not load accounts',
  'users.stale': 'Showing the previous results. Refresh before making changes.',
  'users.usernameRule':
    'Use 3 to 64 English letters, digits, dots, hyphens or underscores.',
  'users.passwordRule':
    'Use 8 to 1024 characters. The password cannot consist entirely of whitespace.',
  'users.confirmPassword': 'Confirm password',
  'users.passwordMismatch': 'The passwords do not match.',
  'users.saveFailed': 'Could not save. Review the message and try again.',
  'users.created': 'User created.',
  'users.updated': 'Account settings saved.',
  'users.passwordReset':
    'Password reset. The account must sign in again with the new password.',
  'users.selfChanged': 'Your account has changed. Sign in again.',
  'users.editHint': 'Changing the username ends all sessions for this account.',
  'users.selfHint':
    'You cannot disable your current account. Change your own password from the account menu in the upper right.',
  'users.disableTitle': 'Confirm account disable',
  'users.disableHint':
    'Disabling {username} prevents sign-in and immediately ends all existing sessions.',
  'users.resetTitle': 'Confirm password reset',
  'users.resetHint':
    'Resetting the password for {username} ends all sessions for that account.',
  'users.confirm': 'Confirm',
  'users.back': 'Back to editing',
  'users.previewHint':
    'This synthetic preview demonstrates account actions without saving real passwords. Sample accounts use preview-only to sign in.',
  'setup.title': 'Administrator setup',
  'setup.description':
    'Welcome to Tongyan Syslog. Create an administrator account to start configuring your log center.',
  'setup.cardTitle': 'Create the first administrator',
  'setup.cardHint':
    'No accounts have been configured. Set a username and password, then continue to sign in.',
  'setup.submit': 'Create administrator and continue',
  'setup.restricted': 'Complete setup from the server itself',
  'setup.restrictedHint':
    'The first administrator can only be created through the server’s local HTTPS entry. A controlled tunnel can be used for remote installation. Normal remote sign-in is available after setup.',
  'setup.unavailable': 'Setup status is temporarily unavailable',
  'setup.unavailableHint':
    'The system status has not been confirmed. Retry or check the service configuration. No accounts or storage will be changed at this stage.',
  'setup.completed':
    'Administrator created. Sign in with the account you just set up.',
  'setup.previewCompleted':
    'Setup preview complete. Sign in with your sample username and preview-only.',
  'setup.previewHint':
    'This is a synthetic setup demonstration. Use a sample username only. Entered passwords are not sent or saved. After setup, sign in with the sample username and preview-only.',
  'setup.loginHint':
    'On first use, the system guides you through creating an administrator. Administrators configure subsequent accounts.',
  'error.usernameConflict': 'This username is already in use. Choose another.',
  'error.userVersion':
    'The account was changed by another action. Refresh the list and edit it again.',
  'error.lastAdmin': 'At least one enabled administrator must remain.',
  'error.selfDisable': 'You cannot disable your current account.',
  'error.selfReset':
    'Use the account menu in the upper right to change your own password.',
  'error.setupCompleted':
    'The administrator has already been set up. Return to sign in.',
  'error.setupLocal': 'Complete setup through the server’s secure local entry.',
} satisfies Record<keyof typeof source, string>;
