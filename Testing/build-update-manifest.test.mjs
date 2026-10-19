import { strict as assert } from 'node:assert';
import { test } from 'node:test';
import { buildUpdateManifest } from '../.github/build-update-manifest.mjs';

function release(tag, overrides = {}) {
  const asset = name => ({ name, state: 'uploaded', size: 123, browser_download_url: `https://github.com/Exelsioro/EDActivityOverlay/releases/download/${tag}/${name}` });
  return {
    tag_name: tag, draft: false, prerelease: tag.includes('-'),
    body: 'Must not be mirrored',
    assets: [asset(`EDActivityOverlay_Setup_${tag.replace(/^v/, '')}.exe`), asset('SHA256SUMS.txt'), asset('portable.zip')],
    ...overrides
  };
}

test('publishes stable and beta channels with installer and checksum URLs', () => {
  const manifest = buildUpdateManifest([release('v1.3.0'), release('v1.3.1-beta.1')]);
  assert.deepEqual(manifest.map(item => item.prerelease), [false, true]);
  assert.equal(manifest[1].assets.length, 2);
  assert.equal(manifest[1].assets[0].name, 'EDActivityOverlay_Setup_1.3.1-beta.1.exe');
  assert.equal(manifest[0].body, undefined);
});

test('does not publish drafts or incomplete uploads', () => {
  assert.deepEqual(buildUpdateManifest([
    release('v1.3.0', { draft: true }), release('v1.4.0', { assets: [] })
  ]), []);
});

test('preserves the GitHub prerelease flag even for a stable-looking tag', () => {
  assert.equal(buildUpdateManifest([release('v1.3.0', { prerelease: true })])[0].prerelease, true);
});

test('rejects unexpected API payloads instead of publishing an empty catalog', () => {
  assert.throws(() => buildUpdateManifest({ message: 'Rate limit exceeded' }));
});
