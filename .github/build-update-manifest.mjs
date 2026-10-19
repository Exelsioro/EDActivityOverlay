import { mkdir, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

// Publish only public update metadata, never release bodies or authentication data.
export function buildUpdateManifest(releases) {
  if (!Array.isArray(releases)) throw new Error('Expected a GitHub release list');
  return releases.filter(release => !release.draft && typeof release.tag_name === 'string')
    .map(release => {
      const installer = `EDActivityOverlay_Setup_${release.tag_name.replace(/^v/, '')}.exe`;
      return {
        tag_name: release.tag_name,
        draft: false,
        prerelease: Boolean(release.prerelease),
        assets: (release.assets ?? [])
          .filter(asset => asset.state === 'uploaded' && [installer, 'SHA256SUMS.txt'].includes(asset.name))
          .map(({ name, state, size, browser_download_url }) => ({ name, state, size, browser_download_url }))
      };
    }).filter(release => release.assets.length === 2);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  let input = '';
  for await (const chunk of process.stdin) input += chunk;
  const manifest = buildUpdateManifest(JSON.parse(input));
  const destination = process.argv[2];
  if (!destination) throw new Error('Provide the output path');
  await mkdir(dirname(destination), { recursive: true });
  await writeFile(destination, JSON.stringify(manifest), 'utf8');
  console.log(`Published update metadata for ${manifest.length} complete releases`);
}
