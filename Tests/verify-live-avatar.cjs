// Optional live storage regression. Creates one uniquely named test avatar,
// overwrites it with real JPEG/WebP/PNG images, and trashes it in finally.
// Never prints the configured Web App URL, base64 payloads or secrets.
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { createHash, randomUUID } = require('node:crypto');
const assert = require('node:assert/strict');

async function main() {
  const settings = JSON.parse(readFileSync(join(__dirname, '..', 'appsettings.json'), 'utf8').replace(/^\uFEFF/, ''));
  const endpoint = process.env.MYLIFE_AVATAR_WEBAPP_URL || settings.GoogleDrive?.WebAppUrl;
  if (!endpoint || new URL(endpoint).hostname !== 'script.google.com') throw new Error('Configured avatar Web App is unavailable.');
  const fixtures = JSON.parse(readFileSync(join(__dirname, 'fixtures', 'avatar-replacements.json'), 'utf8'));
  const email = `mylife-avatar-validation-${randomUUID().replaceAll('-', '')}@example.com`;
  let fileId = null;
  const post = async (body) => {
    const response = await fetch(endpoint, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body), signal: AbortSignal.timeout(45000),
    });
    if (!response.ok) throw new Error(`Avatar Web App HTTP ${response.status}`);
    return response.json();
  };
  try {
    for (let index = 0; index < fixtures.length; index++) {
      const fixture = fixtures[index];
      const bytes = Buffer.from(fixture.base64, 'base64');
      const checksum = createHash('md5').update(bytes).digest('hex');
      const result = await post({ action: 'upload', email,
        existingFileId: fileId, contentType: fixture.contentType, fileBase64: fixture.base64 });
      if (!result.success) throw new Error(`Upload ${index + 1} failed: ${result.message || 'storage failure'}`);
      // Retain the ID for cleanup even when later assertions fail.
      const previousId = fileId;
      fileId ??= result.fileId;
      assert.ok(fileId);
      assert.equal(result.fileId, fileId, 'Drive ID changed');
      assert.equal(result.md5Checksum, checksum, 'Drive checksum does not match uploaded media');
      assert.equal(result.contentType, fixture.contentType, 'Drive MIME does not match uploaded media');
      console.log(JSON.stringify({ upload: index + 1, operation: previousId ? 'overwrite' : 'create',
        fileId, contentType: result.contentType, bytes: bytes.length, checksumVerified: true }));
      // Download the original media through Drive, not a thumbnail/CDN preview.
      const download = await fetch(`https://drive.google.com/uc?export=download&id=${encodeURIComponent(fileId)}&t=${Date.now()}`, {
        signal: AbortSignal.timeout(45000),
      });
      const downloaded = Buffer.from(await download.arrayBuffer());
      if (!download.ok || createHash('md5').update(downloaded).digest('hex') !== checksum)
        throw new Error(`Original Drive media download verification failed after upload ${index + 1} (HTTP ${download.status})`);
      console.log(JSON.stringify({ upload: index + 1, originalMediaDownloadVerified: true }));
    }
  } finally {
    if (fileId) {
      const cleanup = await post({ action: 'delete', email, fileId });
      console.log(JSON.stringify({ cleanup: cleanup.success && cleanup.deleted ? 'trashed-test-avatar' : 'FAILED', fileId }));
      if (!cleanup.success || !cleanup.deleted) throw new Error('Test avatar cleanup failed; inspect the printed file ID.');
    }
  }
}

main().catch(error => { console.error(error.message); process.exitCode = 1; });
