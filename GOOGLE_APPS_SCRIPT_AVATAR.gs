/**
 * MyLife avatar storage Web App.
 *
 * Setup:
 * 1. Set script property AVATAR_FOLDER_ID to the destination Drive folder ID.
 * 2. Enable the Advanced Google service "Drive API", version v2, identifier Drive.
 *    Set runtimeVersion to V8 in appsscript.json (see AVATAR_FIX_REPORT.md).
 * 3. Deploy as a Web App that executes as the owner and configure access for
 *    the backend deployment that calls it.
 *
 * Contract:
 * - upload: { action, email, fileBase64, contentType, existingFileId? }
 * - delete: { action, email, fileId? }
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
  return jsonResponse_({ success: true, service: 'mylife-avatar-storage', contractVersion: 'drive-v2-verified-overwrite-v2' });
}

function doPost(event) {
  var lock = LockService.getScriptLock();
  try {
    lock.waitLock(30000);
    var body = parseBody_(event);
    var action = String(body.action || '').toLowerCase();
    if (action === 'upload') return uploadAvatar_(body);
    if (action === 'delete') return deleteAvatar_(body);
    return jsonResponse_({ success: false, message: 'Unsupported action.' });
  } catch (error) {
    console.error(error && error.stack ? error.stack : error);
    return jsonResponse_({ success: false, message: 'Avatar storage request failed.' });
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
