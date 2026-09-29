#!/usr/bin/env node
/**
 * Marble world generator / downloader.
 *
 * Needs WORLDLABS_API_KEY at User scope. Set it without the value landing in history:
 *     [Environment]::SetEnvironmentVariable('WORLDLABS_API_KEY', (Read-Host 'Paste key'), 'User')
 * then open a new terminal.
 *
 * Usage
 *   node marble.mjs credits                 # free: proves the key works, shows the balance
 *   node marble.mjs depth2rgb depthpano/depthroom-depth.png "a red lacquered temple hall" [--seed N]
 *                                           # depth pano + text -> painted pano   (~80 credits)
 *   node marble.mjs pano-world worlds-out/_panos/<file>.png ["text"] [--model marble-1.1]
 *                                           # painted pano -> world              (1,500 credits)
 *   node marble.mjs generate "a quiet cathedral of glass and moss" [--plus] [--name "..."]
 *   node marble.mjs get <world_id>
 *   node marble.mjs download <world_id> [--tier 100k|500k|full_res|all]
 *   node marble.mjs export <world_id> --asset splats --format ply
 *   node marble.mjs samples                 # free example worlds, no key needed
 *
 * Why this exists: the API exposes THREE splat tiers (100k / 500k / full_res) where the web
 * export UI only offers two, and it returns semantics_metadata — metric_scale_factor and
 * ground_plane_offset — which is precisely the per-world scale/ground data that had to be
 * hand-measured for muse-infinity. Generating through the API means never measuring it again.
 */

import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const BASE = 'https://api.worldlabs.ai/marble/v1';
const OUT = path.resolve('worlds-out');

const key = () => {
  const k = process.env.WORLDLABS_API_KEY;
  if (!k) {
    console.error('WORLDLABS_API_KEY is not set.\n' +
      '  Get one: https://platform.worldlabs.ai  (billing needs a payment method + credits)\n' +
      "  Then:    [Environment]::SetEnvironmentVariable('WORLDLABS_API_KEY', (Read-Host 'Paste key'), 'User')\n" +
      '           and open a new terminal.');
    process.exit(1);
  }
  return k;
};

async function api(pathname, { method = 'GET', body } = {}) {
  const res = await fetch(`${BASE}${pathname}`, {
    method,
    headers: {
      'WLT-Api-Key': key(),
      ...(body ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  let json = {};
  try { json = text ? JSON.parse(text) : {}; } catch { json = { raw: text.slice(0, 400) }; }
  if (!res.ok) throw new Error(`${res.status} ${method} ${pathname}: ${json.detail || json.message || json.raw || 'failed'}`);
  return json;
}

const arg = (flag, fallback = null) => {
  const i = process.argv.indexOf(flag);
  return i > -1 && process.argv[i + 1] ? process.argv[i + 1] : fallback;
};
const has = (flag) => process.argv.includes(flag);

// ---------------------------------------------------------------- generate

async function generate(prompt) {
  const model = has('--plus') ? 'marble-1.1-plus' : arg('--model', 'marble-1.1');
  const displayName = arg('--name', prompt.slice(0, 60));
  console.log(`generating "${displayName}"  model=${model}`);
  console.log('(a full world takes roughly 5 minutes)');
  const op = await api('/worlds:generate', {
    method: 'POST',
    body: {
      display_name: displayName.slice(0, 64),
      model,
      world_prompt: { type: 'text', text_prompt: prompt },
      permission: PRIVATE,
    },
  });
  return finishWorld(op);
}

// Visibility cannot be changed after creation, so it is always sent, never defaulted.
const PRIVATE = { public: false, allow_id_access: false, allowed_readers: [], allowed_writers: [] };

// Never retried: a second worlds:generate is a second paid world (there is no idempotency key).
async function poll(operationId, label, everyMs = 10_000) {
  const started = Date.now();
  for (;;) {
    await new Promise(r => setTimeout(r, everyMs));
    const cur = await api(`/operations/${operationId}`);
    const secs = ((Date.now() - started) / 1000).toFixed(0);
    const status = cur.metadata?.progress?.status ?? 'PENDING';
    process.stdout.write(`\r  ${label}  ${secs} s  ${status}          `);
    if (cur.done) {
      console.log(`\n  done in ${secs} s` + (cur.cost ? `, cost ${cur.cost.total_credits} credits` : ''));
      if (cur.error) throw new Error(`operation failed: ${JSON.stringify(cur.error)}`);
      return cur;
    }
  }
}

async function finishWorld(op) {
  console.log(`operation ${op.operation_id}`);
  const cur = await poll(op.operation_id, 'world');
  // The operation's response is a snapshot that may carry nulls; the world record is the truth.
  const id = cur.response?.world_id ?? cur.response?.id ?? cur.metadata?.world_id;
  save(id, 'operation.json', JSON.stringify(cur, null, 2));
  return get(id);
}

// ---------------------------------------------------------------- panoramas

function base64File(file) {
  const ext = path.extname(file).slice(1).toLowerCase();
  return { source: 'data_base64', data_base64: fs.readFileSync(file).toString('base64'), extension: ext };
}

// A depth PNG needs z_min / z_max to be decoded; DepthPanoBake writes them beside it as .json.
async function depthToRgb(depthFile, prompt) {
  const sidecar = depthFile.replace(/\.png$/i, '.json');
  if (!fs.existsSync(sidecar)) throw new Error(`no sidecar ${sidecar} (z_min / z_max) -- rebake the depth pano`);
  const { z_min, z_max } = JSON.parse(fs.readFileSync(sidecar, 'utf8'));
  const seed = arg('--seed') ? Number(arg('--seed')) : undefined;
  console.log(`depth -> rgb  ${path.basename(depthFile)}  z ${z_min.toFixed(3)}..${z_max.toFixed(3)}`);
  console.log(`  "${prompt}"`);
  const op = await api('/pano:depth_to_rgb', {
    method: 'POST',
    body: { depth_pano_image: base64File(depthFile), z_min, z_max, text_prompt: prompt,
            ...(Number.isInteger(seed) ? { seed } : {}) },
  });
  console.log(`operation ${op.operation_id}`);
  const cur = await poll(op.operation_id, 'pano', 3_000);
  await savePano(cur, path.basename(depthFile, '.png'), { prompt, depth: depthFile, z_min, z_max, seed });
}

// Measured 29 Sep 2026: the URL arrives at response.assets.imagery.pano_url (a World-shaped
// response), NOT at response.pano_url where the spec's PanoDepthToRgbResult puts it. Read both.
async function savePano(cur, stemBase, extra = {}) {
  const url = cur.response?.pano_url ?? cur.response?.assets?.imagery?.pano_url;
  if (!url) throw new Error('operation finished with no pano url');
  const stem = `${stemBase}-${cur.operation_id.slice(0, 8)}`;
  const dest = path.join(OUT, '_panos', `${stem}.png`);
  await fetchTo(url, dest);
  fs.writeFileSync(path.join(OUT, '_panos', `${stem}.json`),
    JSON.stringify({ ...extra, operation: cur }, null, 2));
  console.log(`  next: node marble.mjs pano-world ${path.relative(process.cwd(), dest)}`);
  return dest;
}

// Downloads the result of a finished operation again, free, without re-running anything.
async function fetchPano(operationId) {
  const cur = await api(`/operations/${operationId}`);
  if (!cur.done) throw new Error(`operation ${operationId} is not done yet`);
  return savePano(cur, arg('--name', 'pano'));
}

async function panoWorld(panoFile, prompt) {
  const model = arg('--model', 'marble-1.1');   // never Plus by default: it grows the world
  const displayName = arg('--name', path.basename(panoFile, '.png')).slice(0, 64);
  console.log(`pano -> world  ${path.basename(panoFile)}  model=${model}`);
  const op = await api('/worlds:generate', {
    method: 'POST',
    body: {
      display_name: displayName,
      model,
      world_prompt: { type: 'image', image_prompt: base64File(panoFile), is_pano: true,
                      ...(prompt ? { text_prompt: prompt } : {}) },
      permission: PRIVATE,
      tags: ['musexr', 'depthroom'],
    },
  });
  const world = await finishWorld(op);
  await download(world.world_id ?? world.id, '500k');
  return world;
}

async function credits() {
  const res = await api('/credits');
  console.log(`remaining credits: ${res.remaining_credits}  (~$${(res.remaining_credits / 1250).toFixed(2)})`);
}

// ---------------------------------------------------------------- fetch / download

async function get(worldId) {
  const res = await api(`/worlds/${worldId}`);
  const world = res.world ?? res;
  world.id ??= world.world_id;
  save(world.id, 'world.json', JSON.stringify(world, null, 2));
  report(world);
  return world;
}

async function download(worldId, tier = 'all') {
  const world = await get(worldId);
  if (world.model) console.log(`  model ${world.model}`);
  const urls = world.assets?.splats?.spz_urls ?? {};
  const tiers = tier === 'all' ? Object.keys(urls) : [tier];

  for (const t of tiers) {
    if (!urls[t]) { console.log(`  no ${t} url`); continue; }
    await fetchTo(urls[t], path.join(OUT, worldId, `${worldId}_${t}.spz`));
  }
  const collider = world.assets?.mesh?.collider_mesh_url;
  if (collider) await fetchTo(collider, path.join(OUT, worldId, `${worldId}_collider.glb`));
  const pano = world.assets?.imagery?.pano_url;
  if (pano) await fetchTo(pano, path.join(OUT, worldId, `${worldId}_pano.png`));

  // The bit worth keeping: this is the data muse-infinity measured by hand.
  const sm = world.assets?.splats?.semantics_metadata;
  if (sm) {
    save(worldId, 'semantics.json', JSON.stringify(sm, null, 2));
    console.log(`\n  metric_scale_factor : ${sm.metric_scale_factor}`);
    console.log(`  ground_plane_offset : ${sm.ground_plane_offset}`);
    console.log('  -> in Unity: scale the splat by metric_scale_factor, drop it by');
    console.log('     ground_plane_offset, and rotate 180 degrees about X (marble_raw_opencv).');
  }
}

async function exportAsset(worldId) {
  const asset = arg('--asset', 'splats');
  const format = arg('--format', 'ply');
  console.log(`requesting export asset_type=${asset} format=${format}`);
  const res = await api(`/worlds/${worldId}:export`, {
    method: 'POST',
    body: { asset_type: asset, format },
  });
  console.log(JSON.stringify(res, null, 2));
  if (asset === 'mesh') console.log('NOTE: high-quality mesh can take ~1 hour, rate limited to 4/hour.');
}

async function fetchTo(url, dest) {
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  const res = await fetch(url);
  if (!res.ok) { console.log(`  FAILED ${res.status} ${path.basename(dest)}`); return; }
  const buf = Buffer.from(await res.arrayBuffer());
  fs.writeFileSync(dest, buf);
  console.log(`  ${path.basename(dest)}  ${(buf.length / 1048576).toFixed(1)} MB`);
}

function save(worldId, name, contents) {
  const dir = path.join(OUT, worldId);
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, name), contents);
}

function report(world) {
  console.log(`\nworld ${world.id}`);
  console.log(`  ${world.world_marble_url ?? ''}`);
  const urls = world.assets?.splats?.spz_urls ?? {};
  console.log(`  splat tiers available: ${Object.keys(urls).join(', ') || 'none yet'}`);
  console.log(`  collider mesh : ${world.assets?.mesh?.collider_mesh_url ? 'yes' : 'no'}`);
  console.log(`  hq mesh       : ${world.assets?.mesh?.hq_mesh_url ? 'yes' : 'not exported'}`);
}

// ---------------------------------------------------------------- free samples

const SAMPLES = [
  'rustic_kitchen_with_natural_light',
  'elegant_library_with_fireplace',
  'modern_house_with_lush_landscaping',
  'narrow_european_cobblestone_lane',
  'warm_traditional_kitchen_interior',
];

async function samples() {
  const tier = arg('--tier', '500k');
  console.log(`downloading ${SAMPLES.length} free sample worlds at ${tier} (no API key needed)`);
  for (const s of SAMPLES) {
    const url = `https://wlt-ai-cdn.art/example_exports/${s}/${s}_${tier}.spz`;
    await fetchTo(url, path.join(OUT, '_samples', `${s}_${tier}.spz`));
  }
}

// ---------------------------------------------------------------- main

const cmd = process.argv[2];
const rest = process.argv[3];

try {
  if (cmd === 'credits') await credits();
  else if (cmd === 'depth2rgb' && rest && process.argv[4]) await depthToRgb(rest, process.argv[4]);
  else if (cmd === 'fetch-pano' && rest) await fetchPano(rest);
  else if (cmd === 'pano-world' && rest) await panoWorld(rest, process.argv[4] && !process.argv[4].startsWith('--') ? process.argv[4] : null);
  else if (cmd === 'generate' && rest) await generate(rest);
  else if (cmd === 'get' && rest) await get(rest);
  else if (cmd === 'download' && rest) await download(rest, arg('--tier', 'all'));
  else if (cmd === 'export' && rest) await exportAsset(rest);
  else if (cmd === 'samples') await samples();
  else {
    console.log(fs.readFileSync(new URL(import.meta.url), 'utf8')
      .split('\n').slice(1, 28).join('\n').replace(/^ \*ω?\/?/gm, '').replace(/^ \* ?/gm, ''));
  }
} catch (e) {
  console.error('error:', e.message);
  process.exit(1);
}
