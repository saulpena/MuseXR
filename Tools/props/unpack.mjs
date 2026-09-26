/**
 * Unpack a .glb into a .gltf + .bin + one image file per texture, named by the texture's role.
 *
 *   node unpack.mjs <in.glb> <out-dir>
 *
 * Why: glTFast stores textures EMBEDDED in a .glb as raw uncompressed sub-assets (GltfImporter
 * adds m_Gltf textures straight to the asset), so they reach the headset as RGBA32 -- 93 MB in the
 * build report for the four peach props. Images that exist as files in Assets/ are loaded through
 * Unity's own TextureImporter instead (EditorDownloadProvider -> AssetDatabase.LoadAssetAtPath),
 * which compresses them for Android. Same model, same materials; only where the pixels live changes.
 *
 * Roles come from how each material uses the texture, so the Unity side can set sRGB correctly:
 * basecolor/emissive are colour (sRGB), normal/metalrough/occlusion are data (linear).
 */
import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import path from 'node:path';
import fs from 'node:fs';

const [, , input, outDir] = process.argv;
if (!input || !outDir) { console.error('usage: node unpack.mjs <in.glb> <out-dir>'); process.exit(1); }
const name = path.basename(input, path.extname(input));
const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
const doc = await io.read(input);

const role = new Map();
for (const m of doc.getRoot().listMaterials()) {
  const slots = [['basecolor', m.getBaseColorTexture()], ['normal', m.getNormalTexture()],
                 ['metalrough', m.getMetallicRoughnessTexture()], ['occlusion', m.getOcclusionTexture()],
                 ['emissive', m.getEmissiveTexture()]];
  for (const [r, t] of slots) if (t && !role.has(t)) role.set(t, r);
}
const used = new Set();
for (const t of doc.getRoot().listTextures()) {
  const ext = t.getMimeType() === 'image/png' ? 'png' : 'jpg';
  let base = `${name}_${role.get(t) ?? 'texture'}`;
  let uri = `${base}.${ext}`; let i = 2;
  while (used.has(uri)) uri = `${base}${i++}.${ext}`;
  used.add(uri);
  t.setURI(uri);
  console.log(`  ${uri}  (${role.get(t) ?? 'unused?'})`);
}
doc.getRoot().listBuffers().forEach(b => b.setURI(`${name}.bin`));
fs.mkdirSync(outDir, { recursive: true });
await io.write(path.join(outDir, `${name}.gltf`), doc);
console.log(`${name}: wrote ${name}.gltf + ${name}.bin + ${used.size} images to ${outDir}`);
