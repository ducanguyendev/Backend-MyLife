const { test } = require('node:test');
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const { createHash } = require('node:crypto');
const vm = require('node:vm');
const fixtures = JSON.parse(readFileSync(join(__dirname, 'fixtures/avatar-replacements.json'), 'utf8'));

// The complete deployable source runs against one Drive with three isolated
// roots. Assertions inspect stored media, sharing and actual mutations.
function setup() {
  const properties = { AVATAR_FOLDER_ID: 'accounts', MEMBER_AVATAR_FOLDER_ID: 'members', LIBRARY_FOLDER_ID: 'library' };
  const folders = new Map();
  const files = new Map();
  const updates = [];
  const errors = [];
  let creates = 0;
  let updateMode = 'normal';
  let corruptField = null;
  let failSharing = false;
  let failTrash = false;
  let releases = 0;
  const iterator = values => {
    let index = 0;
    return { hasNext: () => index < values.length, next: () => values[index++] };
  };
  function addFolder(id, name, parents = []) {
    const folder = { id, name, parents, trashed: false,
      getId: () => id, getName: () => name, isTrashed: () => folder.trashed,
      getParents: () => iterator(parents.map(id => folders.get(id))),
      setTrashed: value => { folder.trashed = value; },
      getFiles: () => iterator([...files.values()].filter(file => !file.trashed && file.parents.includes(id))),
      getFoldersByName: name => iterator([...folders.values()].filter(folder => !folder.trashed && folder.name === name && folder.parents.includes(id))),
      createFolder: name => addFolder(`folder-${folders.size}`, name, [id]),
      createFile: blob => { creates++; return add(`created-${creates}`, blob.name, blob.bytes, blob.type, [id]); },
    };
    folders.set(id, folder); return folder;
  }
  function add(id, name, bytes, type = 'image/jpeg', parents = ['members']) {
    const file = { id, name, bytes: Buffer.from(bytes), type, parents, trashed: false, sharing: null,
      getId: () => id, getName: () => file.name, isTrashed: () => file.trashed,
      getParents: () => iterator(file.parents.map(id => folders.get(id))),
      setName: value => { file.name = value; return file; },
      setTrashed: value => { if (failTrash) throw new Error('Trash failed'); file.trashed = value; },
      setSharing: (access, permission) => {
        if (failSharing) throw new Error('Sharing failed'); file.sharing = { access, permission };
      },
    };
    files.set(id, file); return file;
  }
  for (const [id, name] of [['accounts', 'Account_Avatars'], ['members', 'Member_Avatars'], ['library', 'MyLife_Library'], ['outside', 'Other']]) addFolder(id, name);
  const context = vm.createContext({
    console: { log: () => {}, warn: () => {}, error: error => errors.push(String(error)) },
    LockService: { getScriptLock: () => ({ waitLock: () => {}, releaseLock: () => { releases++; } }) },
    PropertiesService: { getScriptProperties: () => ({ getProperty: name => properties[name] }) },
    ContentService: { MimeType: { JSON: 'json' }, createTextOutput: text => ({ setMimeType: () => JSON.parse(text) }) },
    Utilities: {
      DigestAlgorithm: { MD5: 'md5' },
      base64Decode: text => [...Buffer.from(text, 'base64')].map(value => value > 127 ? value - 256 : value),
      newBlob: (bytes, type, name) => ({ bytes: Buffer.from(bytes), type, name }),
      computeDigest: (algorithm, bytes) => [...createHash(algorithm).update(Buffer.from(bytes)).digest()].map(value => value > 127 ? value - 256 : value),
    },
    DriveApp: {
      getFolderById: id => { if (!folders.has(id)) throw new Error('Folder missing'); return folders.get(id); },
      getFileById: id => { if (!files.has(id)) throw new Error('File missing'); return files.get(id); },
      Access: { ANYONE_WITH_LINK: 'public' }, Permission: { VIEW: 'view' },
    },
    Drive: { Files: {
      update: (metadata, id, blob, options) => {
        assert.equal(metadata.name, undefined, 'Drive v2 uses title');
        assert.equal(options.supportsAllDrives, true);
        assert.equal(metadata.title, blob.name);
        updates.push({ id, title: metadata.title, bytes: Buffer.from(blob.bytes), type: metadata.mimeType });
        if (updateMode === 'failure') throw new Error('Drive update failed');
        if (updateMode !== 'noop') Object.assign(files.get(id), { name: metadata.title, bytes: Buffer.from(blob.bytes), type: metadata.mimeType });
      },
      get: (id, options) => {
        assert.equal(options.supportsAllDrives, true);
        assert.equal(options.fields, 'id,md5Checksum,mimeType,fileSize');
        const file = files.get(id);
        const metadata = { id, md5Checksum: createHash('md5').update(file.bytes).digest('hex'), mimeType: file.type, fileSize: String(file.bytes.length) };
        if (corruptField) metadata[corruptField] = 'incorrect';
        return metadata;
      },
    } },
  });
  new vm.Script(readFileSync(join(__dirname, '..', 'GOOGLE_APPS_SCRIPT_AVATAR.gs'), 'utf8'), { filename: 'Code.gs' }).runInContext(context);
  const post = body => context.doPost({ postData: { contents: JSON.stringify(body) } });
  const upload = (index = 0, existingFileId = null, override = {}) => post({
    action: 'member_avatar_upload', memberId: 15, contentType: fixtures[index].contentType,
    fileBase64: fixtures[index].base64, existingFileId, ...override,
  });
  const remove = fileId => post({ action: 'member_avatar_delete', memberId: 15, fileId });
  return { properties, folders, files, updates, errors, add, post, upload, remove, diagnostics: () => context.doGet(),
    creates: () => creates, releases: () => releases, updateMode: mode => { updateMode = mode; },
    corrupt: field => { corruptField = field; }, failSharing: () => { failSharing = true; }, failTrash: () => { failTrash = true; } };
}

test('first member upload creates one stable file, verifies bytes and enables public image sharing', () => {
  const drive = setup(); const result = drive.upload();
  assert.equal(result.success, true);
  const file = drive.files.get(result.fileId);
  assert.equal(file.name, 'member_avatar_15');
  assert.deepEqual(file.parents, ['members']);
  assert.deepEqual(file.bytes, Buffer.from(fixtures[0].base64, 'base64'));
  assert.deepEqual(file.sharing, { access: 'public', permission: 'view' });
  assert.equal(result.md5Checksum, createHash('md5').update(file.bytes).digest('hex'));
  assert.equal(result.fileSize, file.bytes.length);
  assert.equal(result.url, `https://lh3.googleusercontent.com/d/${result.fileId}`);
  assert.equal(result.directUrl, result.url);
  assert.equal(drive.creates(), 1); assert.equal(drive.releases(), 1);
});

test('A through E and 100 replacements retain X, latest bytes/MIME and exactly one live member file', () => {
  const drive = setup(); let id = null;
  for (let index = 0; index < 100; index++) {
    const fixture = fixtures[index % 5]; const result = drive.upload(index % 5, id);
    assert.equal(result.success, true); id ??= result.fileId; assert.equal(result.fileId, id);
    assert.deepEqual(drive.files.get(id).bytes, Buffer.from(fixture.base64, 'base64'));
    assert.equal(drive.files.get(id).type, fixture.contentType);
    assert.equal([...drive.files.values()].filter(file => !file.trashed).length, 1);
  }
  assert.equal(drive.creates(), 1); assert.equal(drive.updates.length, 99);
  assert.equal(drive.upload(4, id).fileId, id, 'reselecting the same image still performs a verified overwrite');
  assert.equal(drive.updates.length, 100);
});

test('legacy lookup overwrites by stable member ID and cleans only its duplicates in the member root', () => {
  const drive = setup();
  drive.add('legacy', 'member_avatar_15.jpg', [1]); drive.add('duplicate', 'member_avatar_15.webp', [2]);
  drive.add('another-member', 'member_avatar_150', [3]); drive.add('outside', 'member_avatar_15', [4], 'image/jpeg', ['accounts']);
  const result = drive.upload(1);
  assert.equal(result.success, true); assert.equal(result.fileId, 'legacy'); assert.equal(drive.creates(), 0);
  assert.equal(drive.files.get('duplicate').trashed, true);
  assert.equal(drive.files.get('another-member').trashed, false); assert.equal(drive.files.get('outside').trashed, false);
});

test('explicit X wins over an earlier legacy match, even after X was renamed', () => {
  const drive = setup(); drive.add('legacy', 'member_avatar_15', [1]); drive.add('X', 'renamed', [2]);
  assert.equal(drive.upload(2, 'X').fileId, 'X'); assert.equal(drive.files.get('X').name, 'member_avatar_15');
  assert.equal(drive.files.get('legacy').trashed, true); assert.equal(drive.creates(), 0);
});

for (const kind of ['missing', 'accounts', 'library', 'outside', 'trashed']) {
  test(`unavailable/outside explicit X (${kind}) fails without creating or falling back`, () => {
    const drive = setup(); drive.add('legacy', 'member_avatar_15', [1]);
    if (kind !== 'missing') drive.add('X', 'member_avatar_15', [2], 'image/jpeg', [kind === 'trashed' ? 'members' : kind]).trashed = kind === 'trashed';
    assert.equal(drive.upload(1, 'X').success, false);
    assert.equal(drive.creates(), 0); assert.equal(drive.updates.length, 0); assert.equal(drive.files.get('legacy').trashed, false);
  });
}

test('missing/conflicting/trashed member root cannot mutate Account Avatar or Library', () => {
  for (const mode of ['missing', 'accounts', 'library', 'trashed']) {
    const drive = setup();
    if (mode === 'missing') delete drive.properties.MEMBER_AVATAR_FOLDER_ID;
    else if (mode === 'trashed') drive.folders.get('members').trashed = true;
    else drive.properties.MEMBER_AVATAR_FOLDER_ID = ` ${mode} `;
    assert.equal(drive.upload().success, false); assert.equal(drive.remove().success, false); assert.equal(drive.creates(), 0);
    assert.equal(drive.folders.get('accounts').trashed, false); assert.equal(drive.folders.get('library').trashed, false);
  }
  const drive = setup(); drive.properties.LIBRARY_FOLDER_ID = ' members ';
  assert.equal(drive.post({ action: 'library_create_album_folder', albumId: 1 }).success, false);
  assert.equal(drive.folders.get('members').trashed, false);
});

test('member ID is the identity, rejects invalid/rounded numbers, and preserves exact decimal strings', () => {
  const drive = setup();
  for (const memberId of [null, 0, -1, 1.5, '01', '1/2', '1e3', 'Nguyễn Văn A', true, 9007199254740992]) {
    assert.equal(drive.upload(0, null, { memberId }).success, false);
  }
  assert.equal(drive.creates(), 0);
  const result = drive.upload(0, null, { memberId: '9223372036854775807', fullName: 'Ignored', fileName: 'ignored.jpg' });
  assert.equal(result.success, true); assert.equal(drive.files.get(result.fileId).name, 'member_avatar_9223372036854775807');
});

test('member avatar validates signatures/size/MIME, normalizes JPG and accepts GIF', () => {
  const drive = setup();
  for (const override of [{ contentType: 'image/svg+xml' }, { contentType: 'image/png' },
    { fileBase64: Buffer.from('<html>bad</html>').toString('base64') }, { fileBase64: '' },
    { fileBase64: Buffer.alloc(5 * 1024 * 1024 + 1).toString('base64') }]) assert.equal(drive.upload(0, null, override).success, false);
  assert.equal(drive.creates(), 0);
  const jpg = drive.upload(0, null, { contentType: ' IMAGE/JPG ' }); assert.equal(jpg.success, true); assert.equal(jpg.contentType, 'image/jpeg');
  const gif = drive.upload(0, jpg.fileId, { contentType: 'image/gif', fileBase64: Buffer.from('GIF89a-test').toString('base64') });
  assert.equal(gif.success, true); assert.equal(gif.fileId, jpg.fileId); assert.equal(gif.contentType, 'image/gif');
});

for (const field of ['id', 'md5Checksum', 'mimeType', 'fileSize']) {
  test(`member success requires read-back ${field}; first-file failure is compensated`, () => {
    const drive = setup(); drive.corrupt(field); const result = drive.upload();
    assert.equal(result.success, false); assert.equal(result.cleanupSuccess, true);
    assert.equal(drive.files.get(result.fileId).trashed, true);
  });
}

test('failed/no-op overwrite never creates a replacement or removes the tracked file/duplicates', () => {
  for (const mode of ['failure', 'noop', 'verification']) {
    const drive = setup(); const first = drive.upload(); drive.add('duplicate', 'member_avatar_15.png', [2]);
    if (mode === 'verification') drive.corrupt('fileSize'); else drive.updateMode(mode);
    assert.equal(drive.upload(1, first.fileId).success, false);
    assert.equal(drive.creates(), 1); assert.equal(drive.files.get(first.fileId).trashed, false);
    assert.equal(drive.files.get('duplicate').trashed, false);
    if (mode !== 'verification') assert.deepEqual(drive.files.get(first.fileId).bytes, Buffer.from(fixtures[0].base64, 'base64'));
    drive.updateMode('normal'); drive.corrupt(null); assert.equal(drive.upload(2, first.fileId).fileId, first.fileId);
  }
});

test('sharing/cleanup failure reports failure; overwrite failure never trashes an existing avatar', () => {
  for (const existing of [false, true]) {
    const drive = setup(); const id = existing ? drive.upload().fileId : null;
    drive.failSharing(); const result = drive.upload(1, id);
    assert.equal(result.success, false); assert.equal(drive.files.get(result.fileId).trashed, !existing);
  }
  const drive = setup(); drive.corrupt('md5Checksum'); drive.failTrash(); const result = drive.upload();
  assert.equal(result.success, false); assert.equal(result.cleanupSuccess, false); assert.ok(result.fileId);
  assert.equal(drive.files.get(result.fileId).trashed, false);
});

test('delete targets X and legacy duplicates, is idempotent, and leaves other members/roots intact', () => {
  const drive = setup(); drive.add('X', 'renamed', [1]); drive.add('duplicate', 'member_avatar_15.jpg', [2]);
  drive.add('other-member', 'member_avatar_16', [3]); drive.add('account', 'member_avatar_15', [4], 'image/jpeg', ['accounts']);
  drive.add('library-photo', 'member_avatar_15', [5], 'image/jpeg', ['library']);
  for (let index = 0; index < 2; index++) { const result = drive.remove('X'); assert.equal(result.success, true); assert.equal(result.deleted, true); }
  assert.equal(drive.files.get('duplicate').trashed, true);
  for (const id of ['other-member', 'account', 'library-photo']) assert.equal(drive.files.get(id).trashed, false);
});

test('invalid delete X fails without legacy cleanup; delete without ID cleans legacy only', () => {
  const drive = setup(); drive.add('legacy', 'member_avatar_15', [1]); drive.add('outside', 'member_avatar_15', [2], 'image/jpeg', ['accounts']);
  for (const id of ['missing', 'outside']) { assert.equal(drive.remove(id).success, false); assert.equal(drive.files.get('legacy').trashed, false); }
  assert.equal(drive.remove(null).deleted, true); assert.equal(drive.files.get('outside').trashed, false);
  assert.equal(drive.remove(null).deleted, false);
});

test('complete dispatcher supports all three contracts; diagnostics expose versions without folder IDs', () => {
  const drive = setup(); const member = drive.upload();
  const account = drive.post({ action: 'upload', email: 'user@gmail.com', fileBase64: fixtures[0].base64, contentType: 'image/jpeg' });
  const album = drive.post({ action: 'library_create_album_folder', albumId: 1 });
  const photo = drive.post({ action: 'library_upload_photo', folderId: album.folderId, fileName: `photo_${'a'.repeat(32)}.jpg`, fileBase64: fixtures[0].base64, contentType: 'image/jpeg' });
  for (const result of [member, account, album, photo]) assert.equal(result.success, true);
  assert.deepEqual(drive.files.get(member.fileId).parents, ['members']); assert.deepEqual(drive.files.get(account.fileId).parents, ['accounts']);
  assert.equal(drive.post({ action: 'delete', email: 'user@gmail.com', fileId: account.fileId }).success, true);
  assert.equal(drive.post({ action: 'library_delete_photo', folderId: album.folderId, fileId: photo.fileId }).success, true);
  assert.equal(drive.post({ action: 'library_delete_album', folderId: album.folderId }).success, true);
  assert.equal(drive.files.get(member.fileId).trashed, false);
  const diagnostic = drive.diagnostics(); assert.equal(diagnostic.contractVersion, 'drive-v2-verified-overwrite-v2');
  assert.equal(diagnostic.avatarContractVersion, diagnostic.contractVersion);
  assert.equal(diagnostic.memberAvatarContractVersion, 'member-avatar-v1-drive-v2-verified-overwrite');
  assert.equal(diagnostic.libraryContractVersion, 'library-phase1-v1');
  for (const [key, id] of Object.entries(drive.properties)) {
    assert.equal(Object.hasOwn(diagnostic, key), false);
    assert.equal(Object.values(diagnostic).includes(id), false);
  }
  assert.equal(drive.post({ action: 'unknown' }).success, false);
});

