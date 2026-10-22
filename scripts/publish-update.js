/* ==========================================================================
   PowerPlugin — Update-Liste signieren (beim Release)
   Wie bei Clavichat: Signiert die Update-Liste (update.json) mit dem privaten
   Schlüssel und legt sie samt Paket in den Zielordner. Den Rest erledigt der
   Release-Workflow (.github/workflows/release.yml): Er ruft dieses Skript auf
   und hängt beide Dateien an das GitHub-Release, von dem die App sie lädt.

   Schlüssel: im Workflow aus dem Secret UPDATE_SIGNING_KEY, lokal aus
   %USERPROFILE%\.powerplugin\update-key.pem (scripts/update-key.js).

   Die Neuerungen im Popup stammen aus dem Abschnitt der Version in
   CHANGELOG.md (anderer Ort: POWERPLUGIN_CHANGELOG).

     powershell -ExecutionPolicy Bypass -File scripts\build-release.ps1
     node scripts/publish-update.js dist\release
   ========================================================================== */
'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');

const ROOT = path.join(__dirname, '..');
const { KEY_FILE, publicKeyOf } = require('./update-key');
const SERVICE = path.join(ROOT, 'src', 'PowerPlugin.App', 'Updates', 'UpdateService.cs');

// Die App zeigt höchstens 12 Stichpunkte zu je 400 Zeichen und schneidet hart
// ab (UpdateManifestReader) – deshalb hier vorher an einer Wortgrenze kürzen.
// Der Text des GitHub-Releases behält den vollen Wortlaut.
const MAX_NOTES = 12;
const MAX_NOTE = 400;

function fail(message) {
  console.error('Abbruch: ' + message);
  process.exit(1);
}

function shorten(text) {
  if (text.length <= MAX_NOTE) return text;
  const cut = text.slice(0, MAX_NOTE - 1);
  const sentence = cut.lastIndexOf('. ');
  if (sentence > MAX_NOTE * 0.6) return cut.slice(0, sentence + 1);
  const space = cut.lastIndexOf(' ');
  return (space > MAX_NOTE / 2 ? cut.slice(0, space) : cut).replace(/[\s,;:–(„"-]+$/, '') + '…';
}

function sha256(bytes) {
  return crypto.createHash('sha256').update(bytes).digest('hex');
}

/** Stichpunkte der Version aus CHANGELOG.md, ohne Markdown. */
function changelogNotes(v) {
  const file = process.env.POWERPLUGIN_CHANGELOG || path.join(ROOT, 'CHANGELOG.md');
  const lines = fs.readFileSync(file, 'utf8').replace(/\r/g, '').split('\n');
  const start = lines.findIndex((l) => l === '## ' + v || l.indexOf('## ' + v + ' ') === 0);
  if (start < 0) fail('In ' + file + ' fehlt der Abschnitt „## ' + v + '“.');
  const notes = [];
  for (let i = start + 1; i < lines.length && lines[i].indexOf('## ') !== 0; i++) {
    const l = lines[i];
    if (/^- /.test(l)) notes.push(l.slice(2).trim());
    else if (/^\s+\S/.test(l) && notes.length) notes[notes.length - 1] += ' ' + l.trim();
  }
  if (!notes.length) fail('Der Abschnitt „## ' + v + '“ in CHANGELOG.md hat keine Stichpunkte.');
  if (notes.length > MAX_NOTES) console.warn('Hinweis: Die App zeigt nur die ersten ' + MAX_NOTES + ' von ' + notes.length + ' Stichpunkten.');
  return notes.slice(0, MAX_NOTES)
    .map((n) => n.replace(/\*\*(.+?)\*\*/g, '$1').replace(/\*([^*\s][^*]*)\*/g, '$1').replace(/`([^`]+)`/g, '$1').replace(/\[([^\]]+)\]\([^)]+\)/g, '$1'))
    .map(shorten);
}

const target = process.argv[2];
if (!target) fail('Zielordner fehlt: node scripts/publish-update.js <Ordner>');

const props = fs.readFileSync(path.join(ROOT, 'Directory.Build.props'), 'utf8');
const match = /<Version>(\d{1,4}\.\d{1,4}\.\d{1,6})<\/Version>/.exec(props);
if (!match) fail('Keine Version (x.y.z) in Directory.Build.props.');
const version = match[1];

const zipName = 'PowerPlugin-' + version + '-win-x64.zip';
const zipSrc = path.join(ROOT, 'dist', zipName);
if (!fs.existsSync(zipSrc)) fail('Es fehlt ' + zipSrc + ' – erst scripts\\build-release.ps1.');

let pem = (process.env.UPDATE_SIGNING_KEY || '').trim();
if (!pem) {
  if (process.env.GITHUB_ACTIONS) fail('Das Secret UPDATE_SIGNING_KEY fehlt (Settings > Secrets and variables > Actions).');
  if (!fs.existsSync(KEY_FILE)) fail('Kein privater Schlüssel unter ' + KEY_FILE + ' (node scripts/update-key.js).');
  pem = fs.readFileSync(KEY_FILE, 'utf8');
}
if (!fs.readFileSync(SERVICE, 'utf8').includes(publicKeyOf(pem))) {
  fail('Der öffentliche Schlüssel in UpdateService.cs passt nicht zum privaten Schlüssel.');
}

const zip = fs.readFileSync(zipSrc);
const manifest = {
  app: 'powerplugin',
  version: version,
  date: new Date().toISOString().slice(0, 10),
  notes: changelogNotes(version),
  files: { zip: { name: zipName, size: zip.length, sha256: sha256(zip) } }
};

const data = JSON.stringify(manifest);
const sig = crypto.sign(null, Buffer.from(data, 'utf8'), crypto.createPrivateKey(pem)).toString('base64');
if (!crypto.verify(null, Buffer.from(data, 'utf8'), crypto.createPublicKey(pem), Buffer.from(sig, 'base64'))) fail('Signatur lässt sich nicht prüfen.');

fs.mkdirSync(target, { recursive: true });
// Unverändert nicht neu schreiben: Das Hochladen nach Änderungszeit würde es
// sonst ein zweites Mal übertragen.
const dest = path.join(target, zipName);
if (!(fs.existsSync(dest) && sha256(fs.readFileSync(dest)) === manifest.files.zip.sha256)) fs.copyFileSync(zipSrc, dest);
fs.writeFileSync(path.join(target, 'update.json'), JSON.stringify({ data: data, sig: sig }, null, 1) + '\n');

for (const name of fs.readdirSync(target)) {
  if (/^PowerPlugin-.*\.zip$/.test(name) && name !== zipName) {
    fs.rmSync(path.join(target, name), { force: true });
    console.log('entfernt:', name);
  }
}
console.log('PowerPlugin ' + version + ' signiert und abgelegt in ' + target);
console.log('  zip: ' + zipName + ' (' + (zip.length / 1048576).toFixed(1) + ' MB)');
console.log('Neuerungen: ' + manifest.notes.length + ' Stichpunkte');
