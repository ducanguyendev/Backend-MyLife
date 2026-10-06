/**
 * MyLife Account Avatar, FamilyMember Avatar and Library storage Web App.
 *
 * Setup:
 * 1. Set script property AVATAR_FOLDER_ID to the destination Drive folder ID.
 *    For FamilyMember avatars, set MEMBER_AVATAR_FOLDER_ID to a separate folder.
 *    For Library, also set LIBRARY_FOLDER_ID to a separate Drive root folder.
 *    All three folder IDs must be different.
 * 2. Enable the Advanced Google service "Drive API", version v2, identifier Drive.
 *    Set runtimeVersion to V8 in appsscript.json (see AVATAR_FIX_REPORT.md).
 * 3. Deploy as a Web App that executes as the owner and configure access for
 *    the backend deployment that calls it.
 *
 * Contract:
 * - upload: { action, email, fileBase64, contentType, existingFileId? }
 * - delete: { action, email, fileId? }
 * - member_avatar_upload: { action, memberId, fileBase64, contentType, existingFileId? }
 * - member_avatar_delete: { action, memberId, fileId? }
 * Member identity is member_avatar_<memberId>, independent of names/email.
 * The stable file name is avatar_<normalized email>; existingFileId is the
 * primary identity and lets Drive.Files.update replace bytes without changing
 * the file ID.
 */

var MAX_AVATAR_BYTES = 5 * 1024 * 1024;
var ALLOWED_CONTENT_TYPES = {
  'image/jpeg': true,
  'image/jpg': true,
  'image/png': true,
  'image/webp': true,
  'image/gif': true
};

function doGet() {
  return jsonResponse_({ success: true, service: 'mylife-avatar-storage',
    contractVersion: 'drive-v2-verified-overwrite-v2',
    avatarContractVersion: 'drive-v2-verified-overwrite-v2',
    memberAvatarContractVersion: 'member-avatar-v1-drive-v2-verified-overwrite',
    libraryContractVersion: 'library-phase1-v1' });
}

function doPost(event) {
  var lock = LockService.getScriptLock();
  try {
    lock.waitLock(30000);
    var body = parseBody_(event);
    var action = String(body.action || '').toLowerCase();
    if (action === 'upload') return uploadAvatar_(body);
    if (action === 'delete') return deleteAvatar_(body);
    if (action === 'member_avatar_upload') return uploadMemberAvatar_(body);
    if (action === 'member_avatar_delete') return deleteMemberAvatar_(body);
    if (action === 'library_create_album_folder') return libraryCreateAlbumFolder_(body);
    if (action === 'library_upload_photo') return libraryUploadPhoto_(body);
    if (action === 'library_delete_photo') return libraryDeletePhoto_(body);
    if (action === 'library_delete_album') return libraryDeleteAlbum_(body);
    return jsonResponse_({ success: false, message: 'Unsupported action.' });
  } catch (error) {
    console.error(error && error.stack ? error.stack : error);
    var message = action && action.indexOf('library_') === 0 ? 'Library storage request failed.' :
      action && action.indexOf('member_avatar_') === 0 ? 'Member avatar storage request failed.' : 'Avatar storage request failed.';
    return jsonResponse_({ success: false, message: message });
  } finally {
    try { lock.releaseLock(); } catch (_) {}
  }
}

function uploadAvatar_(body) {
  var email = normalizeEmail_(body.email);
  var contentType = String(body.contentType || '').toLowerCase();
  if (contentType === 'image/jpg') contentType = 'image/jpeg';
  if (!email || !ALLOWED_CONTENT_TYPES[contentType]) {
    return jsonResponse_({ success: false, message: 'Invalid email or content type.' });
  }

  var bytes = Utilities.base64Decode(String(body.fileBase64 || ''));
  if (!bytes.length || bytes.length > MAX_AVATAR_BYTES) {
    return jsonResponse_({ success: false, message: 'Image must be between 1 byte and 5 MB.' });
  }

  var folder = avatarFolder_();
  var stableName = stableFileName_(email);
  var existingFileId = String(body.existingFileId || '').trim();
  var file = existingFileId ? fileInFolder_(folder, existingFileId) : null;

  console.log('existingFileId:', existingFileId);
  console.log('stableName:', stableName);
  console.log('contentType:', contentType);
  console.log('bytes:', bytes.length);

  // An explicit ID must never silently fall back to a different file/create.
  if (existingFileId && !file) {
    throw new Error('Existing avatar file is unavailable or outside the avatar folder.');
  }
  if (!existingFileId) file = firstAccountFile_(folder, stableName);
  console.log('resolvedFileId:', file ? file.getId() : null);

  var blob = Utilities.newBlob(bytes, contentType, stableName);
  if (file) {
    var currentFileId = file.getId();
    console.log('Updating avatar file ' + currentFileId);
    Drive.Files.update(
      { title: stableName, mimeType: contentType },
      currentFileId,
      blob,
      { supportsAllDrives: true }
    );
    file = DriveApp.getFileById(currentFileId);
  } else {
    file = folder.createFile(blob).setName(stableName);
  }

  // Verify the stored media, not just a URL. A no-op/old deployment must not
  // report success while Drive still contains the previous image bytes.
  var metadata = Drive.Files.get(file.getId(), {
    supportsAllDrives: true,
    fields: 'id,md5Checksum,mimeType,fileSize'
  });
  var expectedChecksum = Utilities.computeDigest(Utilities.DigestAlgorithm.MD5, bytes)
    .map(function (value) { return ('0' + ((value + 256) % 256).toString(16)).slice(-2); })
    .join('');
  if (metadata.id !== file.getId() || metadata.md5Checksum !== expectedChecksum ||
      metadata.mimeType !== contentType || Number(metadata.fileSize) !== bytes.length) {
    throw new Error('Stored avatar bytes or MIME type could not be verified.');
  }

  // The backend exposes a direct lh3 URL; this permission is required for
  // unauthenticated browser/mobile image rendering.
  file.setSharing(DriveApp.Access.ANYONE_WITH_LINK, DriveApp.Permission.VIEW);
  trashAccountDuplicates_(folder, stableName, file.getId());
  console.log('Verified avatar file ' + file.getId() + ' checksum=' + metadata.md5Checksum + ' bytes=' + metadata.fileSize);

  return jsonResponse_({
    success: true,
    fileId: file.getId(),
    md5Checksum: metadata.md5Checksum,
    contentType: metadata.mimeType,
    url: 'https://lh3.googleusercontent.com/d/' + file.getId(),
    fileUrl: 'https://drive.google.com/file/d/' + file.getId() + '/view',
    directUrl: 'https://lh3.googleusercontent.com/d/' + file.getId()
  });
}

function deleteAvatar_(body) {
  var email = normalizeEmail_(body.email);
  if (!email) return jsonResponse_({ success: false, message: 'Invalid email.' });

  var folder = avatarFolder_();
  var fileId = String(body.fileId || '').trim();
  var deleted = false;

  if (fileId) {
    var byId = fileInFolder_(folder, fileId);
    if (!byId) throw new Error('Existing avatar file is unavailable or outside the avatar folder.');
    byId.setTrashed(true);
    deleted = true;
  }

  // Also remove legacy duplicates so they cannot reappear on the next upload.
  var stableName = stableFileName_(email);
  var files = folder.getFiles();
  while (files.hasNext()) {
    var candidate = files.next();
    if (isAccountFileName_(candidate.getName(), stableName)) {
      candidate.setTrashed(true);
      deleted = true;
    }
  }

  return jsonResponse_({ success: true, deleted: deleted });
}

function avatarFolder_() {
  var folderId = PropertiesService.getScriptProperties().getProperty('AVATAR_FOLDER_ID');
  if (!folderId) throw new Error('AVATAR_FOLDER_ID script property is missing.');
  return DriveApp.getFolderById(folderId);
}

function fileInFolder_(folder, fileId) {
  try {
    var file = DriveApp.getFileById(fileId);
    if (file.isTrashed()) return null;
    var parents = file.getParents();
    while (parents.hasNext()) {
      if (parents.next().getId() === folder.getId()) return file;
    }
  } catch (error) {
    console.warn('Cannot resolve avatar file:', fileId, error && error.message ? error.message : error);
  }
  return null;
}

function firstAccountFile_(folder, stableName) {
  var files = folder.getFiles();
  var first = null;
  while (files.hasNext()) {
    var candidate = files.next();
    if (isAccountFileName_(candidate.getName(), stableName)) {
      if (!first) first = candidate;
    }
  }
  return first;
}

function trashAccountDuplicates_(folder, stableName, keptFileId) {
  var files = folder.getFiles();
  while (files.hasNext()) {
    var candidate = files.next();
    if (candidate.getId() !== keptFileId && isAccountFileName_(candidate.getName(), stableName)) {
      candidate.setTrashed(true);
    }
  }
}

function stableFileName_(email) {
  return 'avatar_' + email.replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '');
}

function isAccountFileName_(name, stableName) {
  var normalized = String(name || '').toLowerCase();
  return normalized === stableName || normalized.indexOf(stableName + '.') === 0;
}

function normalizeEmail_(value) {
  var email = String(value || '').trim().toLowerCase();
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email) ? email : '';
}

function parseBody_(event) {
  if (!event || !event.postData || !event.postData.contents) return {};
  return JSON.parse(event.postData.contents);
}

function jsonResponse_(value) {
  return ContentService.createTextOutput(JSON.stringify(value))
    .setMimeType(ContentService.MimeType.JSON);
}

// FamilyMember avatars have their own root and stable member-ID identity.
function memberAvatarFolder_() {
  var properties = PropertiesService.getScriptProperties();
  var folderId = String(properties.getProperty('MEMBER_AVATAR_FOLDER_ID') || '').trim();
  if (!folderId) throw new Error('MEMBER_AVATAR_FOLDER_ID script property is missing.');
  if (folderId === String(properties.getProperty('AVATAR_FOLDER_ID') || '').trim()) {
    throw new Error('Member avatar and account avatar must use separate folders.');
  }
  if (folderId === String(properties.getProperty('LIBRARY_FOLDER_ID') || '').trim()) {
    throw new Error('Member avatar and Library must use separate folders.');
  }
  var folder = DriveApp.getFolderById(folderId);
  if (folder.isTrashed()) throw new Error('Member avatar folder is trashed.');
  return folder;
}

function memberAvatarStableName_(value) {
  // Strings preserve future Int64 IDs; unsafe JS numbers must not be rounded.
  if (typeof value === 'number' && !Number.isSafeInteger(value)) throw new Error('Invalid member ID.');
  var id = String(value || '').trim();
  if (!/^[1-9][0-9]*$/.test(id)) throw new Error('Invalid member ID.');
  return 'member_avatar_' + id;
}

function uploadMemberAvatar_(body) {
  var stableName = memberAvatarStableName_(body.memberId);
  var contentType = String(body.contentType || '').trim().toLowerCase();
  if (contentType === 'image/jpg') contentType = 'image/jpeg';
  if (!ALLOWED_CONTENT_TYPES[contentType]) throw new Error('Invalid member avatar content type.');
  var bytes = Utilities.base64Decode(String(body.fileBase64 || ''));
  if (!bytes.length || bytes.length > MAX_AVATAR_BYTES || !libraryMatchingSignature_(bytes, contentType)) {
    throw new Error('Invalid member avatar image bytes or size.');
  }

  var folder = memberAvatarFolder_();
  var existingFileId = String(body.existingFileId || '').trim();
  var file = existingFileId ? fileInFolder_(folder, existingFileId) : firstAccountFile_(folder, stableName);
  if (existingFileId && !file) {
    throw new Error('Existing member avatar is unavailable or outside the member avatar folder.');
  }
  var created = false;
  try {
    var blob = Utilities.newBlob(bytes, contentType, stableName);
    if (file) {
      var currentFileId = file.getId();
      Drive.Files.update(
        { title: stableName, mimeType: contentType },
        currentFileId,
        blob,
        { supportsAllDrives: true }
      );
      file = DriveApp.getFileById(currentFileId);
    } else {
      file = folder.createFile(blob);
      created = true;
    }
    var metadata = Drive.Files.get(file.getId(), {
      supportsAllDrives: true, fields: 'id,md5Checksum,mimeType,fileSize'
    });
    var checksum = Utilities.computeDigest(Utilities.DigestAlgorithm.MD5, bytes)
      .map(function (value) { return ('0' + ((value + 256) % 256).toString(16)).slice(-2); }).join('');
    if (metadata.id !== file.getId() || metadata.md5Checksum !== checksum ||
        metadata.mimeType !== contentType || Number(metadata.fileSize) !== bytes.length) {
      throw new Error('Member avatar media verification failed.');
    }
    file.setSharing(DriveApp.Access.ANYONE_WITH_LINK, DriveApp.Permission.VIEW);
    // Reuse the existing stable-name helpers, constrained to the member folder.
    trashAccountDuplicates_(folder, stableName, file.getId());
    console.log('Member avatar verified:', stableName, file.getId(), contentType, bytes.length);
    var url = 'https://lh3.googleusercontent.com/d/' + file.getId();
    return jsonResponse_({ success: true, fileId: file.getId(), md5Checksum: metadata.md5Checksum,
      contentType: metadata.mimeType, fileSize: Number(metadata.fileSize), url: url, directUrl: url,
      fileUrl: 'https://drive.google.com/file/d/' + file.getId() + '/view' });
  } catch (error) {
    console.error(error && error.stack ? error.stack : error);
    var cleanupSuccess = !created;
    // Never trash a previously tracked file when an overwrite fails. A newly
    // created, unverified file can be compensated without affecting old media.
    if (created && file) {
      try { file.setTrashed(true); cleanupSuccess = true; }
      catch (cleanupError) { console.error('Member avatar cleanup failed:', file.getId(), cleanupError && cleanupError.message); }
    }
    return jsonResponse_({ success: false, fileId: file ? file.getId() : null,
      cleanupSuccess: cleanupSuccess, message: 'Member avatar upload failed.' });
  }
}

function deleteMemberAvatar_(body) {
  var stableName = memberAvatarStableName_(body.memberId);
  var folder = memberAvatarFolder_();
  var fileId = String(body.fileId || '').trim();
  var deleted = false;
  if (fileId) {
    var file = DriveApp.getFileById(fileId);
    var parents = file.getParents();
    var inFolder = false;
    while (parents.hasNext()) {
      if (parents.next().getId() === folder.getId()) inFolder = true;
    }
    if (!inFolder) throw new Error('Member avatar is outside the member avatar folder.');
    // Idempotent retry after Drive trash succeeded but backend DB save failed.
    if (!file.isTrashed()) file.setTrashed(true);
    deleted = true;
  }
  var files = folder.getFiles();
  while (files.hasNext()) {
    var candidate = files.next();
    if (isAccountFileName_(candidate.getName(), stableName)) {
      candidate.setTrashed(true);
      deleted = true;
    }
  }
  return jsonResponse_({ success: true, deleted: deleted });
}

// Library actions are independent of account-avatar identity and overwrite.
var MAX_LIBRARY_PHOTO_BYTES = 5 * 1024 * 1024;
var LIBRARY_EXTENSIONS = {
  'image/jpeg': 'jpg', 'image/png': 'png', 'image/webp': 'webp', 'image/gif': 'gif'
};

function libraryRootFolder_() {
  var properties = PropertiesService.getScriptProperties();
  var folderId = String(properties.getProperty('LIBRARY_FOLDER_ID') || '').trim();
  if (!folderId) throw new Error('LIBRARY_FOLDER_ID script property is missing.');
  if (folderId === String(properties.getProperty('AVATAR_FOLDER_ID') || '').trim()) {
    throw new Error('Library and avatar must use separate root folders.');
  }
  if (folderId === String(properties.getProperty('MEMBER_AVATAR_FOLDER_ID') || '').trim()) {
    throw new Error('Library and member avatar must use separate root folders.');
  }
  var root = DriveApp.getFolderById(folderId);
  if (root.isTrashed()) throw new Error('Library root folder is trashed.');
  return root;
}

function libraryAlbumFolder_(folderId, allowTrashed) {
  var root = libraryRootFolder_();
  var id = String(folderId || '').trim();
  if (!id || id === root.getId()) throw new Error('Invalid album folder ID.');
  var folder = DriveApp.getFolderById(id);
  if (!/^album_[1-9][0-9]*$/.test(folder.getName()) || (!allowTrashed && folder.isTrashed())) {
    throw new Error('Invalid or trashed album folder.');
  }
  var parents = folder.getParents();
  while (parents.hasNext()) {
    if (parents.next().getId() === root.getId()) return folder;
  }
  throw new Error('Album folder is outside the Library root.');
}

function libraryCreateAlbumFolder_(body) {
  // Backend serializes Int64 as a decimal string to avoid JavaScript rounding.
  var albumId = String(body.albumId || '').trim();
  if (!/^[1-9][0-9]*$/.test(albumId)) throw new Error('Invalid album ID.');
  var root = libraryRootFolder_();
  var name = 'album_' + albumId;
  var matches = root.getFoldersByName(name);
  var folder = matches.hasNext() ? matches.next() : root.createFolder(name);
  console.log('Library album folder:', name, folder.getId());
  return jsonResponse_({ success: true, folderId: folder.getId(), folderName: name });
}

function libraryUploadPhoto_(body) {
  var folder = libraryAlbumFolder_(body.folderId, false);
  var contentType = String(body.contentType || '').trim().toLowerCase();
  if (contentType === 'image/jpg') contentType = 'image/jpeg';
  var extension = LIBRARY_EXTENSIONS[contentType];
  var name = String(body.fileName || '');
  if (!extension || !new RegExp('^photo_[a-f0-9]{32}\\.' + extension + '$').test(name)) {
    throw new Error('Invalid Library image MIME or internal filename.');
  }
  var bytes = Utilities.base64Decode(String(body.fileBase64 || ''));
  if (!bytes.length || bytes.length > MAX_LIBRARY_PHOTO_BYTES || !libraryMatchingSignature_(bytes, contentType)) {
    throw new Error('Invalid Library image bytes or size.');
  }
  var file = null;
  try {
    file = folder.createFile(Utilities.newBlob(bytes, contentType, name));
    file.setSharing(DriveApp.Access.ANYONE_WITH_LINK, DriveApp.Permission.VIEW);
    var metadata = Drive.Files.get(file.getId(), {
      supportsAllDrives: true, fields: 'id,md5Checksum,mimeType,fileSize'
    });
    var checksum = Utilities.computeDigest(Utilities.DigestAlgorithm.MD5, bytes)
      .map(function (value) { return ('0' + ((value + 256) % 256).toString(16)).slice(-2); }).join('');
    if (metadata.id !== file.getId() || metadata.md5Checksum !== checksum ||
        metadata.mimeType !== contentType || Number(metadata.fileSize) !== bytes.length) {
      throw new Error('Library photo media verification failed.');
    }
    console.log('Library photo verified:', folder.getId(), file.getId(), contentType, bytes.length);
    return jsonResponse_({ success: true, fileId: file.getId(), md5Checksum: metadata.md5Checksum,
      contentType: metadata.mimeType, fileSize: Number(metadata.fileSize),
      url: 'https://lh3.googleusercontent.com/d/' + file.getId() });
  } catch (error) {
    console.error(error && error.stack ? error.stack : error);
    var cleanupSuccess = false;
    if (file) {
      try { file.setTrashed(true); cleanupSuccess = true; }
      catch (cleanupError) { console.error('Library photo cleanup failed:', file.getId(), cleanupError && cleanupError.stack ? cleanupError.stack : cleanupError); }
    }
    // Preserve the ID for backend compensation when verification/sharing fails.
    return jsonResponse_({ success: false, fileId: file ? file.getId() : null,
      cleanupSuccess: cleanupSuccess, message: 'Library photo upload failed.' });
  }
}

function libraryDeletePhoto_(body) {
  var folder = libraryAlbumFolder_(body.folderId, true);
  var fileId = String(body.fileId || '').trim();
  if (!fileId) throw new Error('Missing Library photo ID.');
  var file = DriveApp.getFileById(fileId);
  var parents = file.getParents();
  while (parents.hasNext()) {
    if (parents.next().getId() === folder.getId()) {
      if (!file.isTrashed()) file.setTrashed(true);
      return jsonResponse_({ success: true, deleted: true });
    }
  }
  throw new Error('Photo is outside the specified Library album.');
}

function libraryDeleteAlbum_(body) {
  var folder = libraryAlbumFolder_(body.folderId, true);
  if (!folder.isTrashed()) folder.setTrashed(true);
  return jsonResponse_({ success: true, deleted: true });
}

function libraryMatchingSignature_(bytes, contentType) {
  function unsigned(index) { return (bytes[index] + 256) % 256; }
  function startsWith(values, offset) {
    offset = offset || 0;
    return bytes.length >= offset + values.length && values.every(function (value, index) { return unsigned(offset + index) === value; });
  }
  if (contentType === 'image/jpeg') return startsWith([255, 216, 255]);
  if (contentType === 'image/png') return startsWith([137, 80, 78, 71, 13, 10, 26, 10]);
  if (contentType === 'image/webp') return startsWith([82, 73, 70, 70]) && startsWith([87, 69, 66, 80], 8);
  if (contentType === 'image/gif') return startsWith([71, 73, 70, 56, 55, 97]) || startsWith([71, 73, 70, 56, 57, 97]);
  return false;
}
