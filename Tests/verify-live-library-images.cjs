// Optional live regression: only creates a unique disposable Drive album.
// App API responses are intercepted, so no application database is modified.
// Uses real storage actions and anonymous Drive rendering in the actual UI.
const assert = require('node:assert/strict');
const { readFileSync, writeFileSync, mkdtempSync } = require('node:fs');
const { join } = require('node:path');
const { tmpdir } = require('node:os');
const { createHash, randomUUID, randomInt } = require('node:crypto');
const puppeteer = require(process.env.PUPPETEER_MODULE || '../../Frontend-MyLife/scratch_puppeteer/node_modules/puppeteer');

async function main() {
  const settings = JSON.parse(readFileSync(join(__dirname, '..', 'appsettings.json'), 'utf8').replace(/^\uFEFF/, ''));
  const endpoint = process.env.MYLIFE_AVATAR_WEBAPP_URL || settings.GoogleDrive?.WebAppUrl;
  if (!endpoint || new URL(endpoint).hostname !== 'script.google.com') throw new Error('Configured Web App is unavailable.');
  const post = async body => {
    const response = await fetch(endpoint, { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body), signal: AbortSignal.timeout(45000) });
    if (!response.ok) throw new Error(`Storage HTTP ${response.status}`);
    const result = await response.json();
    if (!result.success) throw new Error('Storage rejected the test request; inspect Apps Script Executions.');
    return result;
  };
  const artifacts = mkdtempSync(join(tmpdir(), 'mylife-library-live-'));
  const replacements = JSON.parse(readFileSync(join(__dirname, 'fixtures/avatar-replacements.json'), 'utf8'));
  const fixtures = [
    { ...replacements[1], name: 'Dog.webp' }, { ...replacements[0], name: 'A.jpg' },
    { ...replacements[2], name: 'B.png' },
    { name: 'C.gif', contentType: 'image/gif', base64: 'R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7' },
  ];
  const paths = fixtures.map(f => { const path = join(artifacts, f.name); writeFileSync(path, Buffer.from(f.base64, 'base64')); return path; });
  const albumId = (BigInt(Date.now()) * 1000000n + BigInt(randomInt(1000000))).toString();
  let folderId; let browser; let page;
  const photos = []; const errors = []; const now = new Date().toISOString();
  try {
    const folder = await post({ action: 'library_create_album_folder', albumId });
    folderId = folder.folderId; assert.equal(folder.folderName, `album_${albumId}`); assert.ok(folderId);
    console.log(JSON.stringify({ step: 'created-disposable-album', folderId }));
    browser = await puppeteer.launch({ headless: true, executablePath: process.env.BROWSER_EXECUTABLE || 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe' });
    page = await browser.newPage(); await page.setViewport({ width: 1280, height: 1000 });
    page.on('pageerror', e => errors.push(e.message));
    await page.evaluateOnNewDocument(() => {
      localStorage.setItem('app_language', 'vi'); localStorage.setItem('i18nextLng', 'vi');
      window.__originalLinks = []; window.open = (...args) => { window.__originalLinks.push(args); return null; };
      const originalFetch = window.fetch.bind(window);
      window.fetch = (url, options) => {
        if (String(url).includes('/api/library/') && options?.body instanceof FormData)
          window.__files = options.body.getAll('files').map(file => ({ name: file.name, type: file.type, size: file.size }));
        return originalFetch(url, options);
      };
    });
    await page.setRequestInterception(true);
    page.on('request', async request => {
      const url = new URL(request.url());
      if (!url.pathname.startsWith('/api/')) return request.continue();
      const headers = { 'access-control-allow-origin': request.headers().origin || 'http://127.0.0.1:7001',
        'access-control-allow-credentials': 'true', 'access-control-allow-methods': 'GET,POST,PUT,DELETE,OPTIONS',
        'access-control-allow-headers': 'Content-Type,X-Requested-With' };
      const respond = (body, status = 200) => request.respond({ status, headers, contentType: 'application/json', body: status === 204 ? undefined : JSON.stringify(body) });
      if (request.method() === 'OPTIONS') return respond(null, 204);
      if (url.pathname === '/api/me') return respond({ id: 1, email: 'library-live@example.com', name: 'Library Live', role: 'ADMIN', isActive: true, avatarUrl: null });
      if (url.pathname.startsWith('/api/family-tree')) return respond({ success: true, data: [] });
      if (url.pathname === '/api/library/categories') return respond([{ id: 1, name: 'Ảnh tư liệu', slug: 'photos', isDefault: true }]);
      const album = { id: 1, name: 'Disposable live image test', description: null, photoCount: photos.length, coverPhotoUrl: null, createdAt: now, updatedAt: now, photos };
      if (url.pathname === '/api/library/albums') return respond([album]);
      if (url.pathname === '/api/library/albums/1') return respond(album);
      if (url.pathname === '/api/library/albums/1/photos' && request.method() === 'POST') {
        try {
          const selected = await page.evaluate(() => window.__files);
          assert.deepEqual(selected.map(file => file.name), fixtures.map(f => f.name));
          for (const fixture of fixtures) {
            const bytes = Buffer.from(fixture.base64, 'base64');
            const result = await post({ action: 'library_upload_photo', folderId,
              fileBase64: fixture.base64, contentType: fixture.contentType,
              fileName: `photo_${randomUUID().replaceAll('-', '')}.${fixture.name.split('.').at(-1)}` });
            assert.ok(result.fileId); assert.equal(result.contentType, fixture.contentType);
            assert.equal(result.fileSize, bytes.length);
            assert.equal(result.md5Checksum, createHash('md5').update(bytes).digest('hex'));
            const download = await fetch(`https://drive.google.com/uc?export=download&id=${encodeURIComponent(result.fileId)}`, { signal: AbortSignal.timeout(45000) });
            assert.equal(download.ok, true); assert.equal(createHash('md5').update(Buffer.from(await download.arrayBuffer())).digest('hex'), result.md5Checksum);
            photos.push({ id: photos.length + 1, driveFileId: result.fileId, title: null, category: 'photos',
              fileName: fixture.name, contentType: fixture.contentType, fileSize: bytes.length, url: result.url,
              caption: null, description: null, author: null, displayDate: null, sortOrder: 0, takenAt: null, createdAt: now });
            console.log(JSON.stringify({ step: 'uploaded-verified-original', name: fixture.name, contentType: result.contentType, bytes: bytes.length, fileId: result.fileId }));
          }
          return respond(photos, 201);
        } catch (e) { errors.push(e.message); return respond({ code: 'LIBRARY_STORAGE_FAILED' }, 502); }
      }
      return respond([]);
    });
    const click = (text, selector = 'button') => page.evaluate((text, selector) => {
      const control = [...document.querySelectorAll(selector)].find(control => control.textContent.trim() === text);
      if (!control) throw new Error(`Missing control ${text}`); control.click();
    }, text, selector);
    const openLibrary = async () => { await page.waitForFunction(() => document.body.innerText.includes('Thư viện gia đình')); await click('Thư viện gia đình'); };
    await page.goto(process.env.LIBRARY_UI_TEST_URL || 'http://127.0.0.1:7001/FamilyTree', { waitUntil: 'networkidle0' });
    await openLibrary(); await page.waitForFunction(() => [...document.querySelectorAll('button')].some(b => b.textContent.trim() === 'Tải lên tư liệu / ảnh' && !b.disabled));
    await click('Tải lên tư liệu / ảnh'); await page.waitForSelector('[role="dialog"] input[type="file"]');
    await page.select('[role="dialog"] select', '1');
    await (await page.$('[role="dialog"] input[type="file"]')).uploadFile(...paths);
    await page.waitForFunction(() => [...document.querySelectorAll('[role="dialog"] img')].filter(img => img.naturalWidth > 0 && img.src.startsWith('blob:')).length === 4);
    await page.screenshot({ path: join(artifacts, 'previews.png') });
    await click('Tải lên tư liệu / ảnh', '[role="dialog"] button');
    await page.waitForFunction(() => !document.querySelector('[role="dialog"]'), { timeout: 240000 });
    assert.deepEqual(errors, []); assert.equal(photos.length, 4);
    await page.evaluate(() => [...document.querySelectorAll('button')].find(b => b.textContent.trim() === 'Đóng')?.click());
    const rendered = async () => {
      await page.waitForFunction(names => names.every(name => document.querySelector(`img[alt="${name}"]`)?.naturalWidth > 0), { timeout: 60000 }, fixtures.map(f => f.name));
      return page.evaluate(names => names.map(name => { const image = document.querySelector(`img[alt="${name}"]`); return { name, candidate: image.src.includes('/thumbnail?') ? 'thumbnail' : 'primary', width: image.naturalWidth }; }), fixtures.map(f => f.name));
    };
    console.log(JSON.stringify({ step: 'gallery', images: await rendered() }));
    await page.screenshot({ path: join(artifacts, 'gallery.png'), fullPage: true });
    await page.reload({ waitUntil: 'networkidle0' }); await openLibrary();
    console.log(JSON.stringify({ step: 'after-reload', images: await rendered() }));
    for (const photo of photos) {
      await click(photo.fileName, 'h4'); await page.waitForSelector('[role="dialog"]');
      await page.waitForFunction(() => document.querySelector('[role="dialog"] img')?.naturalWidth > 0, { timeout: 60000 });
      await click('Mở ảnh gốc', '[role="dialog"] button');
      assert.deepEqual(await page.evaluate(() => window.__originalLinks.at(-1)), [`https://drive.google.com/file/d/${photo.driveFileId}/view`, '_blank', 'noopener,noreferrer']);
      await page.screenshot({ path: join(artifacts, `lightbox-${photo.fileName}.png`) }); await page.keyboard.press('Escape');
      console.log(JSON.stringify({ step: 'lightbox-and-original-link', name: photo.fileName, passed: true }));
    }
    console.log(JSON.stringify({ passed: true, artifacts, applicationDatabaseTouched: false, note: 'Dog.webp is a generated test fixture; not the user original dog image.' }));
  } finally {
    if (browser) await browser.close();
    if (folderId) {
      const result = await post({ action: 'library_delete_album', folderId });
      assert.equal(result.deleted, true); console.log(JSON.stringify({ cleanup: 'trashed-disposable-test-album', folderId }));
    }
  }
}
main().catch(error => { console.error(error.message); process.exitCode = 1; });
