const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { createHash } = require('node:crypto');
const vm = require('node:vm');
const fixtures = JSON.parse(readFileSync(join(__dirname, 'fixtures/avatar-replacements.json'), 'utf8'));

function setup() {
  const folders = new Map();
  const files = new Map();
  const errors = [];
  const properties = { AVATAR_FOLDER_ID: 'avatars', LIBRARY_FOLDER_ID: 'library' };
  let nextId = 0;
  let corruptMetadata = false;
  let failSharing = false;
  let failTrash = false;
  const iterator = values => {
    let i = 0;
    return { hasNext: () => i < values.length, next: () => values[i++] };
  };
  function addFolder(id, name, parents = []) {
    const folder = { id, name, parents, trashed: false,
      getId: () => id, getName: () => name, isTrashed: () => folder.trashed,
      getParents: () => iterator(parents.map(id => folders.get(id))),
      setTrashed: value => { if (failTrash) throw new Error('Trash failed'); folder.trashed = value; },
      getFoldersByName: name => iterator([...folders.values()].filter(f => f.name === name && !f.trashed && f.parents.includes(id))),
      createFolder: name => addFolder(`folder-${++nextId}`, name, [id]),
      createFile: blob => {
        const fileId = `file-${++nextId}`;
        const file = { id: fileId, bytes: Buffer.from(blob.bytes), type: blob.type, name: blob.name, trashed: false,
          getId: () => fileId, isTrashed: () => file.trashed, getParents: () => iterator([folder]),
          setSharing: () => { if (failSharing) throw new Error('Sharing failed'); },
          setTrashed: value => { if (failTrash) throw new Error('Trash failed'); file.trashed = value; },
        };
        files.set(fileId, file);
        return file;
      },
    };
    folders.set(id, folder);
    return folder;
  }
  addFolder('avatars', 'Account_Avatars');
  addFolder('library', 'MyLife_Library');
  addFolder('outside', 'Other');
  const context = vm.createContext({
    console: { log: () => {}, warn: () => {}, error: error => errors.push(String(error)) },
    LockService: { getScriptLock: () => ({ waitLock: () => {}, releaseLock: () => {} }) },
    PropertiesService: { getScriptProperties: () => ({ getProperty: key => properties[key] }) },
    ContentService: { MimeType: { JSON: 'json' }, createTextOutput: text => ({ setMimeType: () => JSON.parse(text) }) },
    Utilities: { DigestAlgorithm: { MD5: 'md5' }, base64Decode: base64 => [...Buffer.from(base64, 'base64')].map(x => x > 127 ? x - 256 : x),
      newBlob: (bytes, type, name) => ({ bytes: Buffer.from(bytes), type, name }),
      computeDigest: (algorithm, bytes) => [...createHash(algorithm).update(Buffer.from(bytes)).digest()].map(x => x > 127 ? x - 256 : x) },
    DriveApp: {
      getFolderById: id => { if (!folders.has(id)) throw new Error('Folder missing'); return folders.get(id); },
      getFileById: id => { if (!files.has(id)) throw new Error('File missing'); return files.get(id); },
      Access: { ANYONE_WITH_LINK: 'public' }, Permission: { VIEW: 'view' },
    },
    Drive: { Files: { get: id => {
      const file = files.get(id);
      return { id, md5Checksum: corruptMetadata ? 'wrong' : createHash('md5').update(file.bytes).digest('hex'),
        mimeType: file.type, fileSize: String(file.bytes.length) };
    } } },
  });
  new vm.Script(readFileSync(join(__dirname, '..', 'GOOGLE_APPS_SCRIPT_AVATAR.gs'), 'utf8')).runInContext(context);
  const post = body => context.doPost({ postData: { contents: JSON.stringify(body) } });
  const create = albumId => post({ action: 'library_create_album_folder', albumId });
  const upload = (folderId, index = 0, override = {}) => {
    const image = fixtures[index];
    const extension = { 'image/jpeg': 'jpg', 'image/png': 'png', 'image/webp': 'webp' }[image.contentType];
    return post({ action: 'library_upload_photo', folderId, contentType: image.contentType, fileBase64: image.base64,
      fileName: `photo_${String(index + 1).padStart(32, '0')}.${extension}`, ...override });
  };
  return { post, create, upload, folders, files, properties, addFolder, errors,
    corrupt: () => { corruptMetadata = true; }, failSharing: () => { failSharing = true; }, failTrash: () => { failTrash = true; } };
}

test('album creation is idempotent and Int64 identity stays exact', () => {
  const drive = setup();
  const first = drive.create('9223372036854775807');
  const retry = drive.create('9223372036854775807');
  assert.equal(first.success, true);
  assert.equal(first.folderId, retry.folderId);
  assert.equal(first.folderName, 'album_9223372036854775807');
  assert.equal([...drive.folders.values()].filter(f => f.parents.includes('library')).length, 1);
});

test('multiple images go into the album with exact bytes, MIME, MD5 and file size', () => {
  const drive = setup();
  const album = drive.create(15);
  const ids = [];
  for (const index of [0, 2, 1]) {
    const result = drive.upload(album.folderId, index);
    assert.equal(result.success, true);
    const expected = Buffer.from(fixtures[index].base64, 'base64');
    assert.deepEqual(drive.files.get(result.fileId).bytes, expected);
    assert.equal(result.md5Checksum, createHash('md5').update(expected).digest('hex'));
    assert.equal(result.contentType, fixtures[index].contentType);
    assert.equal(result.fileSize, expected.length);
    assert.equal(result.url, `https://lh3.googleusercontent.com/d/${result.fileId}`);
    assert.equal(drive.files.get(result.fileId).getParents().next().getId(), album.folderId);
    ids.push(result.fileId);
  }
  assert.equal(new Set(ids).size, 3);
});

test('jpg alias normalizes to jpeg; GIF signature is supported', () => {
  const drive = setup();
  const album = drive.create(1);
  assert.equal(drive.upload(album.folderId, 0, { contentType: 'image/jpg' }).contentType, 'image/jpeg');
  const gif = drive.upload(album.folderId, 0, { contentType: 'image/gif',
    fileBase64: Buffer.from('GIF89a-test').toString('base64'), fileName: `photo_${'a'.repeat(32)}.gif` });
  assert.equal(gif.success, true);
  assert.equal(gif.contentType, 'image/gif');
});

test('Library cannot use avatar root, avatar folders, outside folders or the Library root itself', () => {
  const drive = setup();
  drive.addFolder('avatar-album', 'album_9', ['avatars']);
  drive.addFolder('outside-album', 'album_9', ['outside']);
  for (const folder of ['avatars', 'avatar-album', 'outside-album', 'library']) {
    assert.equal(drive.upload(folder).success, false);
    assert.equal(drive.post({ action: 'library_delete_album', folderId: folder }).success, false);
    assert.equal(drive.folders.get(folder).trashed, false);
  }
  drive.properties.LIBRARY_FOLDER_ID = 'avatars';
  assert.equal(drive.create(1).success, false);
  assert.equal(drive.files.size, 0);
});

test('missing Library configuration does not touch existing avatar configuration', () => {
  const drive = setup();
  delete drive.properties.LIBRARY_FOLDER_ID;
  assert.equal(drive.create(1).success, false);
  assert.equal(drive.properties.AVATAR_FOLDER_ID, 'avatars');
  assert.equal(drive.folders.get('avatars').trashed, false);
});

test('unverified media/sharing failure trashes the new file and preserves cleanup identity', () => {
  for (const mode of ['corrupt', 'failSharing']) {
    const drive = setup();
    const album = drive.create(1);
    drive[mode]();
    const result = drive.upload(album.folderId);
    assert.equal(result.success, false);
    assert.equal(result.cleanupSuccess, true);
    assert.equal(drive.files.get(result.fileId).trashed, true);
  }
});

test('cleanup failure is reported with a file ID, never a fake success', () => {
  const drive = setup();
  const album = drive.create(1);
  drive.corrupt(); drive.failTrash();
  const result = drive.upload(album.folderId);
  assert.equal(result.success, false);
  assert.equal(result.cleanupSuccess, false);
  assert.ok(result.fileId);
  assert.ok(drive.errors.some(error => error.includes('Library photo cleanup failed')));
});

test('delete is constrained to the correct album and is idempotent for already-trashed items', () => {
  const drive = setup();
  const first = drive.create(1);
  const second = drive.create(2);
  const photo = drive.upload(first.folderId);
  const wrong = drive.post({ action: 'library_delete_photo', folderId: second.folderId, fileId: photo.fileId });
  assert.equal(wrong.success, false);
  assert.equal(drive.files.get(photo.fileId).trashed, false);
  for (let i = 0; i < 2; i++) {
    assert.equal(drive.post({ action: 'library_delete_photo', folderId: first.folderId, fileId: photo.fileId }).deleted, true);
    assert.equal(drive.post({ action: 'library_delete_album', folderId: first.folderId }).deleted, true);
  }
  assert.equal(drive.files.get(photo.fileId).trashed, true);
  assert.equal(drive.folders.get(first.folderId).trashed, true);
  assert.equal(drive.folders.get(second.folderId).trashed, false);
});

test('unsafe name, MIME mismatch, invalid signature, empty and oversized images cannot create files', () => {
  const drive = setup();
  const album = drive.create(1);
  for (const override of [
    { fileName: '../photo.jpg' }, { contentType: 'image/svg+xml' },
    { fileName: `photo_${'a'.repeat(32)}.png` },
    { fileBase64: Buffer.from('<html>not an image</html>').toString('base64') },
    { fileBase64: '' }, { fileBase64: Buffer.alloc(5 * 1024 * 1024 + 1).toString('base64') },
  ]) assert.equal(drive.upload(album.folderId, 0, override).success, false);
  assert.equal(drive.files.size, 0);
});
