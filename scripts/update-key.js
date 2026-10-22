/* ==========================================================================
   PowerPlugin — Schlüssel für signierte Updates
   Legt beim ersten Aufruf ein Ed25519-Schlüsselpaar an und zeigt danach
   immer den öffentlichen Schlüssel (der gehört in UpdateService.cs,
   PublicKey) und den privaten für das GitHub-Secret UPDATE_SIGNING_KEY.

   Der private Schlüssel liegt in %USERPROFILE%\.powerplugin\update-key.pem
   (oder dort, wohin POWERPLUGIN_UPDATE_KEY zeigt). Er gehört nie ins Repo:
   Wer ihn hat, kann Updates für alle ausliefern. Geht er verloren, wird ein
   neuer angelegt; dann müssen alle die nächste Fassung einmal von Hand holen.

   Aufruf: node scripts/update-key.js
   ========================================================================== */
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

const KEY_FILE = process.env.POWERPLUGIN_UPDATE_KEY || path.join(os.homedir(), '.powerplugin', 'update-key.pem');

function publicKeyOf(privatePem) {
  return crypto.createPublicKey(privatePem).export({ type: 'spki', format: 'der' }).toString('base64');
}

if (require.main === module) {
  if (!fs.existsSync(KEY_FILE)) {
    const { privateKey } = crypto.generateKeyPairSync('ed25519');
    fs.mkdirSync(path.dirname(KEY_FILE), { recursive: true });
    fs.writeFileSync(KEY_FILE, privateKey.export({ type: 'pkcs8', format: 'pem' }), { mode: 0o600, flag: 'wx' });
    console.log('Neuer privater Schlüssel: ' + KEY_FILE);
    console.log('Bitte zusätzlich sicher aufbewahren (z. B. Passwortmanager).');
  } else {
    console.log('Privater Schlüssel: ' + KEY_FILE);
  }
  const pem = fs.readFileSync(KEY_FILE, 'utf8');
  console.log('');
  console.log('1) Öffentlicher Schlüssel - gehört in UpdateService.cs (PublicKey):');
  console.log(publicKeyOf(pem));
  console.log('');
  console.log('2) Privater Schlüssel - komplett, mit BEGIN- und END-Zeile, als GitHub-Secret');
  console.log('   UPDATE_SIGNING_KEY eintragen (Settings > Secrets and variables > Actions):');
  console.log(pem.trim());
}

module.exports = { KEY_FILE, publicKeyOf };
