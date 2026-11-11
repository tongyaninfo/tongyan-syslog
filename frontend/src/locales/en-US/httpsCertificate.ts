export default {
  'menu.httpsCertificate': 'HTTPS certificate',
  'httpsCertificate.title': 'Console HTTPS certificate',
  'httpsCertificate.current': 'Active certificate',
  'httpsCertificate.pending': 'Pending certificate',
  'httpsCertificate.noCurrent': 'No active certificate details are available.',
  'httpsCertificate.noPending': 'No certificate is pending.',
  'httpsCertificate.subject': 'Subject',
  'httpsCertificate.fingerprint': 'Fingerprint',
  'httpsCertificate.notBefore': 'Valid from (UTC+8)',
  'httpsCertificate.notAfter': 'Expires at (UTC+8)',
  'httpsCertificate.upload': 'Upload certificate',
  'httpsCertificate.file': 'Certificate file',
  'httpsCertificate.chooseFile': 'Choose certificate file',
  'httpsCertificate.fileHint':
    'Select a .pfx or .p12 file with a private key, up to 256 KiB.',
  'httpsCertificate.password': 'Certificate password (optional)',
  'httpsCertificate.readPasswordFile': 'Read password file',
  'httpsCertificate.passwordFileInvalid':
    'Choose a UTF-8 TXT file up to 4 KiB. The password must be a single line of up to 1024 characters.',
  'httpsCertificate.save': 'Save certificate',
  'httpsCertificate.confirmTitle': 'Save HTTPS certificate?',
  'httpsCertificate.confirmHint':
    'After saving, new HTTPS connections to the console will automatically use the new certificate.',
  'httpsCertificate.pendingNotice':
    'The saved certificate is pending activation.',
  'httpsCertificate.saved':
    'New HTTPS connections use the updated certificate. Existing connections keep the previous certificate until they close. To verify, restart your browser and use a hostname or IP address covered by the certificate.',
  'httpsCertificate.loadFailed': 'Could not load the saved certificate',
  'httpsCertificate.fileRequired': 'Select a valid .pfx or .p12 file.',
  'httpsCertificate.tooLarge': 'The certificate file cannot exceed 256 KiB.',
  'httpsCertificate.invalid':
    'The certificate or password is invalid. Check that the file has a usable private key and the certificate is within its validity period.',
  'httpsCertificate.unavailable':
    'The certificate is unavailable. Check the server configuration.',
  'httpsCertificate.managedExternally':
    'The HTTPS certificate is managed in the server configuration. Update it on the server.',
  'httpsCertificate.selfSigned': 'Service self-signed HTTPS certificate',
  'httpsCertificate.uploaded': 'Uploaded HTTPS certificate',
};
