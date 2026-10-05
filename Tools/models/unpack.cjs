// Unpack a .glb into .gltf + .bin + image files, so Unity imports the images as ordinary textures that
// get the platform's compression (ASTC on Android) and a max size. Inside a .glb, glTFast imports them
// uncompressed: measured 4 Oct 2026, the 20k-triangle tray goddess carried 89.5 MB of RGB24/ARGB32 maps.
//
//   node unpack.cjs <in.glb> <out.gltf>
//
// Uses gltf-transform from MusePico/Tools/tripo (already installed there for decimate.mjs).
const path = require('path');
const modules = path.resolve(__dirname, '../../../MusePico/Tools/tripo/node_modules');
const { NodeIO } = require(path.join(modules, '@gltf-transform/core'));
const { ALL_EXTENSIONS } = require(path.join(modules, '@gltf-transform/extensions'));

(async () => {
  const [input, output] = process.argv.slice(2);
  if (!input || !output) { console.error('usage: node unpack.cjs <in.glb> <out.gltf>'); process.exit(2); }
  const io = new NodeIO().registerExtensions(ALL_EXTENSIONS);
  const doc = await io.read(input);
  const name = path.basename(output, '.gltf');
  // Name each image after the model and its role, so the texture assets are findable in Unity.
  for (const mat of doc.getRoot().listMaterials()) {
    const roles = { baseColor: mat.getBaseColorTexture(), normal: mat.getNormalTexture(),
                    metalRough: mat.getMetallicRoughnessTexture(), occlusion: mat.getOcclusionTexture(),
                    emissive: mat.getEmissiveTexture() };
    for (const [role, tex] of Object.entries(roles)) if (tex) tex.setURI(`${name}_${role}.${tex.getMimeType() === 'image/jpeg' ? 'jpg' : 'png'}`);
  }
  doc.getRoot().listBuffers().forEach((b, i) => b.setURI(i === 0 ? `${name}.bin` : `${name}_${i}.bin`));
  require('fs').mkdirSync(path.dirname(path.resolve(output)), { recursive: true });
  await io.write(output, doc);
  console.log('unpacked', output);
})().catch(e => { console.error(e); process.exit(1); });
