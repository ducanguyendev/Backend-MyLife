const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { createHash } = require('node:crypto');
const vm = require('node:vm');

// Execute the actual Apps Script against an in-memory Drive. Tests inspect
// persisted bytes/metadata and mutations, rather than trusting returned URLs.
function setup() {
  const files = new Map();
  const updates = [];
  const errors = [];
  const logs = [];
  let creates = 0;
  let updateFails = false;
  let updateNoOp = false;
  const iterator = (values) => {
    let index = 0;
    return { hasNext: () => index < values.length, next: () => values[index++] };
  };
  const folder = {
    getId: () => 'avatar-folder',
    getFiles: () => iterator([...files.values()].filter(f => !f.trashed && f.parentIds.includes('avatar-folder'))),
    createFile: (blob) => {
      creates++;
      return add(`created-${creates}`, blob.name, blob.bytes, blob.type);
    },
  };
  function add(id, name, bytes, type = 'image/jpeg', parentIds = ['avatar-folder']) {
    const file = {
      id, name, bytes: Buffer.from(bytes), type, parentIds, trashed: false,
      getId: () => id,
      getName: () => file.name,
      isTrashed: () => file.trashed,
      setName: (value) => { file.name = value; return file; },
      setTrashed: (value) => { file.trashed = value; },
      setSharing: () => {},
      getParents: () => iterator(file.parentIds.map(parent => ({ getId: () => parent }))),
    };
    files.set(id, file);
    return file;
  }
  const context = vm.createContext({
    console: { log: (...values) => logs.push(values.join(' ')), warn: () => {}, error: (e) => errors.push(String(e)) },
    LockService: { getScriptLock: () => ({ waitLock: () => {}, releaseLock: () => {} }) },
    PropertiesService: { getScriptProperties: () => ({ getProperty: () => 'avatar-folder' }) },
    ContentService: {
      MimeType: { JSON: 'json' },
      createTextOutput: (text) => ({ setMimeType: () => JSON.parse(text) }),
    },
    Utilities: {
      DigestAlgorithm: { MD5: 'md5' },
      base64Decode: (base64) => [...Buffer.from(base64, 'base64')],
      newBlob: (bytes, type, name) => ({ bytes: Buffer.from(bytes), type, name }),
      computeDigest: (algorithm, bytes) => [...createHash(algorithm).update(Buffer.from(bytes)).digest()]
        .map(value => value > 127 ? value - 256 : value),
    },
    DriveApp: {
      getFolderById: () => folder,
      getFileById: (id) => {
        if (!files.has(id)) throw new Error('File not found');
        return files.get(id);
      },
      Access: { ANYONE_WITH_LINK: 'public' }, Permission: { VIEW: 'view' },
    },
    Drive: { Files: {
      update: (metadata, id, blob, options) => {
        assert.equal(metadata.title, 'avatar_user_gmail_com');
        assert.equal(metadata.name, undefined, 'v2 requires title');
        assert.equal(options.supportsAllDrives, true);
        updates.push({ id, bytes: Buffer.from(blob.bytes), type: metadata.mimeType });
        if (updateFails) throw new Error('Drive update failed');
        if (!updateNoOp) Object.assign(files.get(id), {
          name: metadata.title, type: metadata.mimeType, bytes: Buffer.from(blob.bytes),
        });
      },
      get: (id) => {
        const file = files.get(id);
        return { id, md5Checksum: createHash('md5').update(file.bytes).digest('hex'),
          mimeType: file.type, fileSize: String(file.bytes.length) };
      },
    } },
  });
  const source = readFileSync(join(__dirname, '..', 'GOOGLE_APPS_SCRIPT_AVATAR.gs'), 'utf8');
  new vm.Script(source, { filename: 'Code.gs' }).runInContext(context);
  const post = (payload) => context.doPost({ postData: { contents: JSON.stringify(payload) } });
  const upload = (bytes, contentType = 'image/jpeg', existingFileId = null) => post({
    action: 'upload', email: 'User@gmail.com', fileBase64: Buffer.from(bytes).toString('base64'),
    contentType, existingFileId,
  });
  return { files, updates, errors, logs, add, upload, post, creates: () => creates,
    failUpdate: () => { updateFails = true; }, resumeUpdates: () => { updateFails = false; },
    noOpUpdate: () => { updateNoOp = true; } };
}

test('100 consecutive uploads always overwrite X with the latest bytes and MIME, including selecting the same image again', () => {
  const drive = setup();
  const formats = ['image/jpeg', 'image/webp', 'image/png', 'image/webp', 'image/jpeg'];
  let id = null;
  for (let index = 0; index < 100; index++) {
    const bytes = [index, 255 - index, 128, 254];
    const mime = formats[index % formats.length];
    const result = drive.upload(bytes, mime, id);
    assert.equal(result.success, true, `upload ${index + 1}`);
    id ??= result.fileId;
    assert.equal(result.fileId, id);
    assert.deepEqual(drive.files.get(id).bytes, Buffer.from(bytes));
    assert.equal(drive.files.get(id).type, mime);
    assert.equal([...drive.files.values()].filter(file => !file.trashed).length, 1);
    if (index > 0) {
      assert.equal(drive.updates[index - 1].id, id);
      assert.deepEqual(drive.updates[index - 1].bytes, Buffer.from(bytes));
      assert.equal(drive.updates[index - 1].type, mime);
    }
  }
  assert.equal(drive.creates(), 1);
  assert.equal(drive.updates.length, 99);
  assert.equal(drive.logs.filter(log => log === `Updating avatar file ${id}`).length, 99);
  const same = drive.upload([99, 156, 128, 254], 'image/jpeg', id);
  assert.equal(same.success, true);
  assert.equal(same.fileId, id);
  assert.equal(drive.updates.length, 100, 'the same image is still overwritten, never returned early');
  assert.equal(drive.creates(), 1);
});

test('overwrite failure after B keeps B and retry C/D/E succeeds using the same ID', () => {
  const drive = setup();
  const first = drive.upload([1]);
  assert.equal(drive.upload([2], 'image/webp', first.fileId).success, true);
  drive.failUpdate();
  assert.equal(drive.upload([3], 'image/png', first.fileId).success, false);
  assert.deepEqual(drive.files.get(first.fileId).bytes, Buffer.from([2]));
  drive.resumeUpdates();
  for (const value of [3, 4, 5]) {
    assert.equal(drive.upload([value], 'image/jpeg', first.fileId).fileId, first.fileId);
    assert.deepEqual(drive.files.get(first.fileId).bytes, Buffer.from([value]));
  }
  assert.equal(drive.creates(), 1);
});

test('A-D: create once, overwrite actual bytes and change JPEG to WebP with the same ID', () => {
  const drive = setup();
  const first = drive.upload([1, 2, 3]);
  assert.equal(first.success, true);
  assert.equal(drive.creates(), 1);
  assert.equal(first.url, `https://lh3.googleusercontent.com/d/${first.fileId}`);
  const second = drive.upload([9, 8, 7, 6], 'image/webp', first.fileId);
  assert.equal(second.success, true);
  assert.equal(first.fileId, second.fileId);
  assert.equal(drive.creates(), 1);
  assert.equal(drive.updates[0].id, first.fileId);
  assert.deepEqual(drive.updates[0].bytes, Buffer.from([9, 8, 7, 6]));
  assert.deepEqual(drive.files.get(first.fileId).bytes, Buffer.from([9, 8, 7, 6]));
  assert.equal(drive.files.get(first.fileId).type, 'image/webp');
});

test('E: update error logs failure, preserves old bytes and never creates a replacement', () => {
  const drive = setup();
  drive.add('existing', 'avatar_user_gmail_com.jpg', [1]);
  drive.add('duplicate', 'avatar_user_gmail_com.png', [2]);
  drive.failUpdate();
  assert.equal(drive.upload([3], 'image/webp', 'existing').success, false);
  assert.equal(drive.creates(), 0);
  assert.deepEqual(drive.files.get('existing').bytes, Buffer.from([1]));
  assert.equal(drive.files.get('duplicate').trashed, false, 'cleanup waits until successful overwrite');
  assert.match(drive.errors[0], /Drive update failed/);
});

test('a successful API no-op cannot hide unchanged Drive bytes behind a fresh URL', () => {
  const drive = setup();
  drive.add('existing', 'avatar_user_gmail_com', [1]);
  drive.noOpUpdate();
  assert.equal(drive.upload([2], 'image/jpeg', 'existing').success, false);
  assert.equal(drive.creates(), 0);
  assert.match(drive.errors[0], /could not be verified/);
});

test('F: legacy duplicates keep one ID, update its bytes, then trash the rest', () => {
  const drive = setup();
  drive.add('legacy', 'avatar_user_gmail_com.jpg', [1]);
  drive.add('duplicate', 'avatar_user_gmail_com.webp', [2]);
  drive.add('other-account', 'avatar_other_gmail_com', [3]);
  const result = drive.upload([4], 'image/webp');
  assert.equal(result.success, true);
  assert.equal(result.fileId, 'legacy');
  assert.equal(drive.creates(), 0);
  assert.equal(drive.files.get('duplicate').trashed, true);
  assert.equal(drive.files.get('other-account').trashed, false);
  assert.deepEqual(drive.files.get('legacy').bytes, Buffer.from([4]));
});

test('explicit ID wins even when an older duplicate is listed first or MIME/name changed', () => {
  const drive = setup();
  drive.add('duplicate', 'avatar_user_gmail_com.jpg', [1]);
  drive.add('selected', 'renamed-avatar', [2], 'image/jpeg', ['other-folder', 'avatar-folder']);
  const result = drive.upload([3], 'image/webp', 'selected');
  assert.equal(result.fileId, 'selected');
  assert.equal(drive.files.get('duplicate').trashed, true);
  assert.equal(drive.files.get('selected').trashed, false);
});

for (const kind of ['missing', 'outside', 'trashed']) {
  test(`invalid explicit ID (${kind}) fails without legacy fallback or file creation`, () => {
    const drive = setup();
    drive.add('legacy', 'avatar_user_gmail_com', [1]);
    if (kind === 'outside') drive.add('bad-id', 'avatar_user_gmail_com', [2], 'image/jpeg', ['elsewhere']);
    if (kind === 'trashed') drive.add('bad-id', 'avatar_user_gmail_com', [2]).trashed = true;
    assert.equal(drive.upload([3], 'image/webp', 'bad-id').success, false);
    assert.equal(drive.creates(), 0);
    assert.equal(drive.updates.length, 0);
    assert.equal(drive.files.get('legacy').trashed, false);
  });
}

test('delete targets the tracked ID and cleans legacy duplicates only in the avatar folder', () => {
  const drive = setup();
  drive.add('tracked', 'renamed-avatar', [1]);
  drive.add('duplicate', 'avatar_user_gmail_com.jpg', [2]);
  drive.add('outside', 'avatar_user_gmail_com', [3], 'image/jpeg', ['elsewhere']);
  assert.equal(drive.post({ action: 'delete', email: 'user@gmail.com', fileId: 'tracked' }).success, true);
  assert.equal(drive.files.get('tracked').trashed, true);
  assert.equal(drive.files.get('duplicate').trashed, true);
  assert.equal(drive.files.get('outside').trashed, false);
});

test('delete with unavailable tracked ID fails instead of claiming success', () => {
  const drive = setup();
  drive.add('legacy', 'avatar_user_gmail_com', [1]);
  assert.equal(drive.post({ action: 'delete', email: 'user@gmail.com', fileId: 'missing' }).success, false);
  assert.equal(drive.files.get('legacy').trashed, false);
});
