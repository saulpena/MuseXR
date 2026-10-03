"""Make one of Skylar's masters as a rigged character with the 3D generator, the way Monet and Picasso were made.

    python model_master.py balance                 free: proves the key and shows credits
    python model_master.py make vincent-van-gogh   generate (multi-image, ~30 cr) -> rig (5 cr) -> download

Input: her four views, muse-infinity/assets/generated/turnarounds/views/<master>/{front,left,right,back}.png
Output: Tools/models/out/<master>/  (task json, textured GLB, rigged FBX + GLB, textures)

Reads MODEL_API_KEY from the environment and never prints it. No retries: a failed paid task is
reported, not silently re-billed.
"""
import base64, json, os, sys, time, urllib.request, pathlib

API = os.environ.get("MODEL_API_BASE", "") + "/v1"
HERE = pathlib.Path(__file__).resolve().parent
VIEWS = HERE.parents[2] / "muse-infinity" / "assets" / "generated" / "turnarounds" / "views"

# Matches the shipped painters: ~40k triangles, one textured mesh with a normal map, A-pose so the
# auto-rigger gets clear limbs. Heights follow the README's (Monet 1.75 m, Picasso 1.70 m).
GENERATE = {"ai_model": "latest", "topology": "triangle", "target_polycount": 40000,
            "should_remesh": True, "should_texture": True, "enable_pbr": True, "pose_mode": "a-pose"}
HEIGHT = {"vincent-van-gogh": 1.70, "socrates": 1.70, "frida-kahlo": 1.60,
          "hilma-af-klint": 1.68, "berthe-morisot": 1.65, "pablo-picasso": 1.70, "claude-monet": 1.75}


def call(method, path, body=None):
    key = os.environ.get("MODEL_API_KEY")
    if not key:
        sys.exit("MODEL_API_KEY is not set")
    req = urllib.request.Request(API + path, method=method,
                                 data=json.dumps(body).encode() if body is not None else None,
                                 headers={"Authorization": "Bearer " + key, "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            return json.loads(r.read())
    except urllib.error.HTTPError as e:
        sys.exit(f"{method} {path} -> HTTP {e.code}: {e.read().decode(errors='replace')[:600]}")


def wait(path, label):
    t0 = time.time()
    while True:
        t = call("GET", path)
        status = t.get("status")
        print(f"  {label}: {status} {t.get('progress', '')}% ({time.time() - t0:.0f}s)", flush=True)
        if status in ("SUCCEEDED", "FAILED", "CANCELED", "EXPIRED"):
            return t
        time.sleep(10)


def download(url, dest):
    dest.parent.mkdir(parents=True, exist_ok=True)
    with urllib.request.urlopen(url, timeout=300) as r:
        dest.write_bytes(r.read())
    print(f"  saved {dest.name} ({dest.stat().st_size // 1024} KB)")


def data_uri(p):
    return "data:image/png;base64," + base64.b64encode(p.read_bytes()).decode()


def make(master):
    views = VIEWS / master
    images = [views / f"{v}.png" for v in ("front", "left", "right", "back")]
    missing = [str(p) for p in images if not p.exists()]
    if missing:
        sys.exit("missing views: " + ", ".join(missing))
    out = HERE / "out" / master
    out.mkdir(parents=True, exist_ok=True)

    print(f"[1/3] generate {master} from 4 views (paid)")
    task = call("POST", "/multi-image-to-3d", dict(GENERATE, image_urls=[data_uri(p) for p in images]))
    gen_id = task["result"]
    gen = wait(f"/multi-image-to-3d/{gen_id}", "generate")
    (out / "generate.json").write_text(json.dumps(gen, indent=1))
    if gen["status"] != "SUCCEEDED":
        sys.exit(f"generate {gen['status']}: {gen.get('task_error')}")
    download(gen["model_urls"]["glb"], out / f"{master}-textured.glb")
    for i, tex in enumerate(gen.get("texture_urls") or []):
        for k, u in tex.items():
            if u:
                download(u, out / f"{master}_{k}_{i}.png")

    print(f"[2/3] rig {master} at {HEIGHT.get(master, 1.7)} m (paid)")
    rig_id = call("POST", "/rigging", {"input_task_id": gen_id, "height_meters": HEIGHT.get(master, 1.7)})["result"]
    rig = wait(f"/rigging/{rig_id}", "rig")
    (out / "rig.json").write_text(json.dumps(rig, indent=1))
    if rig["status"] != "SUCCEEDED":
        sys.exit(f"rig {rig['status']}: {rig.get('task_error')}")

    print("[3/3] download rigged character")
    res = rig.get("result") or {}
    for k, ext in (("rigged_character_fbx_url", "fbx"), ("rigged_character_glb_url", "glb")):
        if res.get(k):
            download(res[k], out / f"{master}-rigged.{ext}")
    print(f"done: {out}  credits used: generate {gen.get('consumed_credits', '?')}, rig {rig.get('consumed_credits', '?')}")


if __name__ == "__main__":
    if len(sys.argv) >= 2 and sys.argv[1] == "balance":
        print(call("GET", "/balance"))
    elif len(sys.argv) >= 3 and sys.argv[1] == "make":
        make(sys.argv[2])
    else:
        sys.exit(__doc__)
