#!/usr/bin/env node
/**
 * Marble world generator / downloader.
 *
 * Everything here is ready to run; the only missing piece is a key:
 *     setx WORLDLABS_API_KEY "wlt-..."      (new terminal afterwards)
 *
 * Usage
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
      '  Then:    setx WORLDLABS_API_KEY "wlt-..."   and open a new terminal.');
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
  const model = has('--plus') ? 'marble-1.1-plus' : 'marble-1.1';
  const displayName = arg('--name', prompt.slice(0, 60));
  console.log(`generating "${displayName}"  model=${model}`);
  console.log('(a full world takes roughly 5 minutes)');

  const op = await api('/worlds:generate', {
    method: 'POST',
    body: {
      display_name: displayName,
      model,
      world_prompt: { type: 'text', text_prompt: prompt },
    },
  });
  console.log(`operation ${op.operation_id}`);

  const started = Date.now();
  for (;;) {
    await new Promise(r => setTimeout(r, 10_000));
    const cur = await api(`/operations/${op.operation_id}`);
    const mins = ((Date.now() - started) / 60000).toFixed(1);
    const status = cur.metadata?.progress?.status ?? 'PENDING';
    process.stdout.write(`\r  ${mins} min  ${status}          `);
    if (cur.error) { console.error('\nfailed:', JSON.stringify(cur.error)); process.exit(1); }
    if (cur.done) {
      console.log('\ndone.');
      const world = cur.response;
      save(world.id, 'world.json', JSON.stringify(world, null, 2));
      report(world);
      return world;
    }
  }
}

// ---------------------------------------------------------------- fetch / download

async function get(worldId) {
  const res = await api(`/worlds/${worldId}`);
  const world = res.world ?? res;
  save(world.id, 'world.json', JSON.stringify(world, null, 2));
  report(world);
  return world;
}

async function download(worldId, tier = 'all') {
  const world = await get(worldId);
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
  if (cmd === 'generate' && rest) await generate(rest);
  else if (cmd === 'get' && rest) await get(rest);
  else if (cmd === 'download' && rest) await download(rest, arg('--tier', 'all'));
  else if (cmd === 'export' && rest) await exportAsset(rest);
  else if (cmd === 'samples') await samples();
  else {
    console.log(fs.readFileSync(new URL(import.meta.url), 'utf8')
      .split('\n').slice(1, 22).join('\n').replace(/^ \*ω?\/?/gm, '').replace(/^ \* ?/gm, ''));
  }
} catch (e) {
  console.error('error:', e.message);
  process.exit(1);
}
