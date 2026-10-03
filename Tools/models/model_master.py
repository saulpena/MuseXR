"""Make one of Skylar's masters as a rigged character with the 3D generator, the way Monet and Picasso were made.

    python model_master.py balance                 free: proves the key and shows credits
    python model_master.py make vincent-van-gogh   original one-shot: 4 bust views -> multi-image (~30 cr) -> rig (5 cr)

Step-by-step commands (every result lands in out/<master>/<tag>/, task id written before waiting):

    concept <master> <tag> --refs a.png,b.png [--model nano-banana-pro] [--multi] [--aspect 9:16] --prompt-file p.txt
        image-to-image: turn bust references into a full-body A-pose concept (3-12 cr).
    gen <master> <tag> (--from-task ID | --images a.png,b.png,...) [--single] [--poly 40000]
        [--geo standard|2k] [--tex-prompt-file f] [--tex-images a,b] [--ai-model latest] [--no-pose]
        multi-image-to-3d (or image-to-3d with --single), remeshed, textured, PBR, A-pose.
    rig <master> <tag> --task ID          auto-rigging of a finished generate task (5 cr).
    retex <master> <tag> --task ID (--prompt-file f | --style-image a.png) [--keep-uv]
    fetch <kind> <task-id> <master> <tag> re-download a task's outputs (free).
    t2i <name> <tag> --prompt-file p.txt [--aspect 9:16] [--remove-bg]   text-to-image prop concept (out/doors/...).
    prop <name> <tag> --image c.png [--poly 12000] [--symmetry on]     static prop via image-to-3d, remeshed, PBR.

Reads MODEL_API_KEY from the environment and never prints it. No retries: a failed paid task is
reported, not silently re-billed.
"""
import argparse, base64, json, os, sys, time, urllib.request, pathlib

API = os.environ.get("MODEL_API_BASE", "") + "/v1"
API2 = os.environ.get("MODEL_API_BASE", "") + "/v2"
HERE = pathlib.Path(__file__).resolve().parent
VIEWS = HERE.parents[2] / "muse-infinity" / "assets" / "generated" / "turnarounds" / "views"

# Matches the shipped painters: ~40k triangles, one textured mesh with a normal map, A-pose so the
# auto-rigger gets clear limbs. Heights follow the README's (Monet 1.75 m, Picasso 1.70 m).
GENERATE = {"ai_model": "latest", "topology": "triangle", "target_polycount": 40000,
            "should_remesh": True, "should_texture": True, "enable_pbr": True, "pose_mode": "a-pose"}
HEIGHT = {"vincent-van-gogh": 1.70, "socrates": 1.70, "frida-kahlo": 1.60,
          "hilma-af-klint": 1.68, "berthe-morisot": 1.65, "pablo-picasso": 1.70, "claude-monet": 1.75}
ENDPOINT = {"multi": "/multi-image-to-3d", "single": "/image-to-3d", "rig": "/rigging",
            "retex": "/retexture", "i2i": "/image-to-image"}


def call(method, path, body=None, base=API):
    key = os.environ.get("MODEL_API_KEY")
    if not key:
        sys.exit("MODEL_API_KEY is not set")
    req = urllib.request.Request(base + path, method=method,
                                 data=json.dumps(body).encode() if body is not None else None,
                                 headers={"Authorization": "Bearer " + key, "Content-Type": "application/json"})
    for attempt in range(3):  # network-level retry for GETs only; POSTs are never retried
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                return json.loads(r.read())
        except urllib.error.HTTPError as e:
            sys.exit(f"{method} {path} -> HTTP {e.code}: {e.read().decode(errors='replace')[:600]}")
        except (urllib.error.URLError, TimeoutError) as e:
            if method != "GET" or attempt == 2:
                raise
            time.sleep(5)


def wait(path, label):
    t0 = time.time()
    last = None
    while True:
        t = call("GET", path)
        status = t.get("status")
        line = f"  {label}: {status} {t.get('progress', '')}%"
        if line != last:
            print(f"{line} ({time.time() - t0:.0f}s)", flush=True); last = line
        if status in ("SUCCEEDED", "FAILED", "CANCELED", "EXPIRED"):
            return t
        time.sleep(10)


def download(url, dest):
    dest.parent.mkdir(parents=True, exist_ok=True)
    with urllib.request.urlopen(url, timeout=300) as r:
        dest.write_bytes(r.read())
    print(f"  saved {dest.name} ({dest.stat().st_size // 1024} KB)")


def data_uri(p):
    p = pathlib.Path(p)
    mime = "image/jpeg" if p.suffix.lower() in (".jpg", ".jpeg") else "image/png"
    return f"data:{mime};base64," + base64.b64encode(p.read_bytes()).decode()


def outdir(master, tag):
    d = HERE / "out" / master / tag
    d.mkdir(parents=True, exist_ok=True)
    return d


def save_generate(t, out, master):
    (out / "task.json").write_text(json.dumps(t, indent=1))
    if t["status"] != "SUCCEEDED":
        sys.exit(f"FAILED {t['status']}: {t.get('task_error')}")
    download(t["model_urls"]["glb"], out / f"{master}-textured.glb")
    for i, tex in enumerate(t.get("texture_urls") or []):
        for k, u in tex.items():
            if u:
                download(u, out / f"{master}_{k}_{i}.png")
    print(f"DONE {out} credits {t.get('consumed_credits', '?')}")


def cmd_concept(a):
    out = outdir(a.master, a.tag)
    body = {"ai_model": a.model, "prompt": pathlib.Path(a.prompt_file).read_text(encoding="utf-8").strip(),
            "reference_image_urls": [data_uri(p) for p in a.refs.split(",")],
            "generate_multi_view": a.multi}
    if not a.multi:
        body["aspect_ratio"] = a.aspect
    tid = call("POST", ENDPOINT["i2i"], body)["result"]
    (out / "task_id.txt").write_text(f"i2i {tid}\n")
    print(f"[concept] {a.master}/{a.tag} task {tid}")
    t = wait(f"{ENDPOINT['i2i']}/{tid}", "concept")
    (out / "task.json").write_text(json.dumps(t, indent=1))
    if t["status"] != "SUCCEEDED":
        sys.exit(f"FAILED {t['status']}: {t.get('task_error')}")
    for i, u in enumerate(t.get("image_urls") or []):
        download(u, out / f"concept_{i}.png")
    print(f"DONE {out} credits {t.get('consumed_credits', '?')}")


def cmd_gen(a):
    out = outdir(a.master, a.tag)
    body = dict(GENERATE, target_polycount=a.poly, ai_model=a.ai_model)
    if a.no_pose:
        body["pose_mode"] = ""
    if a.geo != "standard":
        body["geometry_resolution"] = a.geo
    if a.tex_prompt_file:
        body["texture_prompt"] = pathlib.Path(a.tex_prompt_file).read_text(encoding="utf-8").strip()[:800]
    if a.tex_images:
        imgs = a.tex_images.split(",")
        if len(imgs) == 1:
            body["texture_image_url"] = data_uri(imgs[0])
        else:
            body["texture_image_urls"] = [data_uri(p) for p in imgs]
    kind = "single" if a.single else "multi"
    if a.from_task:
        body["input_task_id"] = a.from_task
    elif a.single:
        body["image_url"] = data_uri(a.images.split(",")[0])
    else:
        body["image_urls"] = [data_uri(p) for p in a.images.split(",")]
    tid = call("POST", ENDPOINT[kind], body)["result"]
    (out / "task_id.txt").write_text(f"{kind} {tid}\n")
    (out / "request.json").write_text(json.dumps({k: (v if not isinstance(v, str) or not v.startswith("data:") else "<data uri>")
                                                   for k, v in body.items() if k not in ("image_urls", "texture_image_urls")}, indent=1))
    print(f"[gen] {a.master}/{a.tag} {kind} task {tid}")
    save_generate(wait(f"{ENDPOINT[kind]}/{tid}", "generate"), out, a.master)


def cmd_retex(a):
    out = outdir(a.master, a.tag)
    body = {"input_task_id": a.task, "enable_pbr": True, "enable_original_uv": a.keep_uv, "ai_model": a.ai_model}
    if a.prompt_file:
        body["text_style_prompt"] = pathlib.Path(a.prompt_file).read_text(encoding="utf-8").strip()[:800]
    if a.style_image:
        body["image_style_url"] = data_uri(a.style_image)
    if a.multiview:
        body["multiview_image_urls"] = [data_uri(p) for p in a.multiview.split(",")]
    tid = call("POST", ENDPOINT["retex"], body)["result"]
    (out / "task_id.txt").write_text(f"retex {tid}\n")
    print(f"[retex] {a.master}/{a.tag} task {tid}")
    save_generate(wait(f"{ENDPOINT['retex']}/{tid}", "retexture"), out, a.master)


def cmd_rig(a):
    out = outdir(a.master, a.tag)
    h = a.height or HEIGHT.get(a.master, 1.7)
    tid = call("POST", ENDPOINT["rig"], {"input_task_id": a.task, "height_meters": h})["result"]
    (out / "task_id.txt").write_text(f"rig {tid}\n")
    print(f"[rig] {a.master}/{a.tag} at {h} m task {tid}")
    rig = wait(f"{ENDPOINT['rig']}/{tid}", "rig")
    (out / "rig.json").write_text(json.dumps(rig, indent=1))
    if rig["status"] != "SUCCEEDED":
        sys.exit(f"FAILED rig {rig['status']}: {rig.get('task_error')}")
    res = rig.get("result") or {}
    for k, ext in (("rigged_character_fbx_url", "fbx"), ("rigged_character_glb_url", "glb")):
        if res.get(k):
            download(res[k], out / f"{a.master}-rigged.{ext}")
    for k, u in (res.get("basic_animations") or {}).items():
        if u and k.endswith("_glb_url") and "armature" not in k.lower():
            download(u, out / f"{a.master}-anim-{k[:-8]}.glb")
    print(f"DONE {out} credits {rig.get('consumed_credits', '?')}")


def cmd_fetch(a):
    out = outdir(a.master, a.tag)
    t = call("GET", f"{ENDPOINT[a.kind]}/{a.id}")
    if a.kind == "i2i":
        for i, u in enumerate(t.get("image_urls") or []):
            download(u, out / f"concept_{i}.png")
    else:
        save_generate(t, out, a.master)


def cmd_t2i(a):
    """text-to-image (3-9 cr). Props: out/<group>/<name>/<tag>/concept_N.png"""
    out = HERE / "out" / a.group / a.name / a.tag
    out.mkdir(parents=True, exist_ok=True)
    body = {"ai_model": a.model, "prompt": pathlib.Path(a.prompt_file).read_text(encoding="utf-8").strip(),
            "aspect_ratio": a.aspect, "remove_background": a.remove_bg}
    tid = call("POST", "/text-to-image", body)["result"]
    (out / "task_id.txt").write_text(f"t2i {tid}\n")
    print(f"[t2i] {a.group}/{a.name}/{a.tag} task {tid}")
    t = wait(f"/text-to-image/{tid}", "t2i")
    (out / "task.json").write_text(json.dumps(t, indent=1))
    if t["status"] != "SUCCEEDED":
        sys.exit(f"FAILED {t['status']}: {t.get('task_error')}")
    for i, u in enumerate(t.get("image_urls") or []):
        download(u, out / f"concept_{i}.png")
    print(f"DONE {out} credits {t.get('consumed_credits', '?')}")


def cmd_prop(a):
    """Static prop (door, frame) from one concept image: image-to-3d, remeshed to a budget, PBR.
    No pose, no rig. Output: out/<group>/<name>/<tag>/<name>.glb + textures."""
    out = HERE / "out" / a.group / a.name / a.tag
    out.mkdir(parents=True, exist_ok=True)
    body = {"ai_model": a.ai_model, "image_url": data_uri(a.image), "should_texture": True, "enable_pbr": True,
            "should_remesh": True, "topology": "triangle", "target_polycount": a.poly,
            "symmetry_mode": a.symmetry, "texture_resolution": "2k", "target_formats": ["glb", "fbx"],
            "origin_at": "bottom", "save_pre_remeshed_model": False}
    if a.geo != "standard":
        body["geometry_resolution"] = a.geo
    if a.tex_prompt_file:
        body["texture_prompt"] = pathlib.Path(a.tex_prompt_file).read_text(encoding="utf-8").strip()[:800]
    tid = call("POST", "/image-to-3d", body)["result"]
    (out / "task_id.txt").write_text(f"single {tid}\n")
    (out / "request.json").write_text(json.dumps({k: v for k, v in body.items() if k != "image_url"} | {"image": a.image}, indent=1))
    print(f"[prop] {a.group}/{a.name}/{a.tag} task {tid}")
    t = wait(f"/image-to-3d/{tid}", "prop")
    (out / "task.json").write_text(json.dumps(t, indent=1))
    if t["status"] != "SUCCEEDED":
        sys.exit(f"FAILED {t['status']}: {t.get('task_error')}")
    for ext in ("glb", "fbx"):
        if t["model_urls"].get(ext):
            download(t["model_urls"][ext], out / f"{a.name}.{ext}")
    for i, tex in enumerate(t.get("texture_urls") or []):
        for k, u in tex.items():
            if u:
                download(u, out / f"{a.name}_{k}_{i}.png")
    print(f"DONE {out} credits {t.get('consumed_credits', '?')}")


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
        print(call("GET", "/balance")); sys.exit()
    if len(sys.argv) >= 3 and sys.argv[1] == "make":
        make(sys.argv[2]); sys.exit()
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("concept"); c.add_argument("master"); c.add_argument("tag")
    c.add_argument("--refs", required=True); c.add_argument("--model", default="nano-banana-pro")
    c.add_argument("--multi", action="store_true"); c.add_argument("--aspect", default="9:16")
    c.add_argument("--prompt-file", required=True); c.set_defaults(f=cmd_concept)
    g = sub.add_parser("gen"); g.add_argument("master"); g.add_argument("tag")
    g.add_argument("--from-task"); g.add_argument("--images"); g.add_argument("--single", action="store_true")
    g.add_argument("--poly", type=int, default=40000); g.add_argument("--geo", default="standard")
    g.add_argument("--tex-prompt-file"); g.add_argument("--tex-images"); g.add_argument("--ai-model", default="latest")
    g.add_argument("--no-pose", action="store_true"); g.set_defaults(f=cmd_gen)
    r = sub.add_parser("rig"); r.add_argument("master"); r.add_argument("tag"); r.add_argument("--task", required=True)
    r.add_argument("--height", type=float); r.set_defaults(f=cmd_rig)
    x = sub.add_parser("retex"); x.add_argument("master"); x.add_argument("tag"); x.add_argument("--task", required=True)
    x.add_argument("--prompt-file"); x.add_argument("--style-image"); x.add_argument("--multiview"); x.add_argument("--keep-uv", action="store_true")
    x.add_argument("--ai-model", default="latest"); x.set_defaults(f=cmd_retex)
    fe = sub.add_parser("fetch"); fe.add_argument("kind", choices=list(ENDPOINT)); fe.add_argument("id")
    fe.add_argument("master"); fe.add_argument("tag"); fe.set_defaults(f=cmd_fetch)
    ti = sub.add_parser("t2i"); ti.add_argument("name"); ti.add_argument("tag"); ti.add_argument("--group", default="doors")
    ti.add_argument("--prompt-file", required=True); ti.add_argument("--model", default="nano-banana-pro")
    ti.add_argument("--aspect", default="1:1"); ti.add_argument("--remove-bg", action="store_true"); ti.set_defaults(f=cmd_t2i)
    pr = sub.add_parser("prop"); pr.add_argument("name"); pr.add_argument("tag"); pr.add_argument("--group", default="doors")
    pr.add_argument("--image", required=True); pr.add_argument("--poly", type=int, default=12000)
    pr.add_argument("--symmetry", default="auto"); pr.add_argument("--geo", default="standard")
    pr.add_argument("--tex-prompt-file"); pr.add_argument("--ai-model", default="latest"); pr.set_defaults(f=cmd_prop)
    a = ap.parse_args(); a.f(a)
