"""GPU splat renderer that mirrors aras-p UnityGaussianSplatting v1.1.1, for visibility pruning.

It renders a view exactly the way the package does, and while it does so it records, for every
splat, the largest amount it ever added to any pixel (alpha * transmittance). A splat whose
largest contribution over every view that matters is below a threshold is invisible to the
visitor and can be deleted.

Fidelity to the package (Shaders/GaussianSplatting.hlsl, SplatUtilities.compute,
RenderGaussianSplats.shader) is the whole point, so these are copied, not approximated:
  * 2D covariance by EWA with the view-space x/y clamp at 1.3 * tan(fov/2), plus 0.3 px low-pass
  * quad = +-2 along each eigen-axis in units of sqrt(2*lambda), lambda2 floored at 0.1
  * fragment alpha = saturate(exp(-|q|^2) * opacity), discarded below 1/255
  * sorted by view depth, blended front to back, colours composited in gamma space

Coordinates are Unity's: the .spz values are used as-is (SPZFileReader applies no axis flip)
and the world object only scales them by worldScale. Cameras follow Unity's
Quaternion.Euler(pitch, yaw, 0), left-handed, +z forward, +y up.
"""
import gzip
import math
import struct

import numpy as np
import torch

DEV = torch.device("cuda")


def load_spz(path, world_scale=1.7):
    raw = gzip.open(path).read()
    magic, ver, n, sh, fb, flags, _ = struct.unpack("<IIIBBBB", raw[:16])
    if magic != 0x5053474E or ver != 2:
        raise ValueError(f"{path}: not SPZ v2 (magic {magic:#x}, version {ver})")
    if sh != 0:
        raise ValueError(f"{path}: SH degree {sh}; only degree 0 is supported here")
    o = 16
    p = np.frombuffer(raw, np.uint8, n * 9, o).reshape(n, 3, 3).astype(np.int32); o += n * 9
    pos = p[..., 0] | (p[..., 1] << 8) | (p[..., 2] << 16)
    pos = np.where(pos & 0x800000, pos - (1 << 24), pos) / float(1 << fb)
    alpha = np.frombuffer(raw, np.uint8, n, o).astype(np.float32) / 255.0; o += n
    col = np.frombuffer(raw, np.uint8, n * 3, o).reshape(n, 3).astype(np.float32); o += n * 3
    scl = np.frombuffer(raw, np.uint8, n * 3, o).reshape(n, 3).astype(np.float32); o += n * 3
    rot = np.frombuffer(raw, np.uint8, n * 3, o).reshape(n, 3).astype(np.float32); o += n * 3
    if o != len(raw):
        raise ValueError(f"{path}: {len(raw) - o} unexpected trailing bytes")

    xyz = rot / 127.5 - 1.0
    w = np.sqrt(np.maximum(0.0, 1.0 - (xyz ** 2).sum(1, keepdims=True)))
    q = np.concatenate([xyz, w], 1)
    q /= np.linalg.norm(q, axis=1, keepdims=True)
    dc = (col / 255.0 - 0.5) / 0.15
    t = lambda a: torch.from_numpy(np.ascontiguousarray(a, dtype=np.float32)).to(DEV)
    return {
        "n": n,
        "pos": t(pos * world_scale),
        "scale": t(np.exp(scl / 16.0 - 10.0) * world_scale),
        "quat": t(q),                       # x, y, z, w
        "opacity": t(alpha),
        "color": t(dc * 0.2820948 + 0.5),   # SH0ToColor
    }


def cov3d(g):
    x, y, z, w = g["quat"].unbind(1)
    r = torch.stack([
        1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y),
        2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x),
        2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y),
    ], 1).view(-1, 3, 3)
    m = r * g["scale"][:, None, :]
    return m @ m.transpose(1, 2)


def camera_basis(yaw_deg, pitch_deg):
    """Rows are the camera's right, up and forward in world space: Quaternion.Euler(pitch, yaw, 0)."""
    y, p = math.radians(yaw_deg), math.radians(pitch_deg)
    ry = np.array([[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]])
    rx = np.array([[1, 0, 0], [0, math.cos(p), -math.sin(p)], [0, math.sin(p), math.cos(p)]])
    rot = ry @ rx
    return rot.T  # columns of rot are right/up/forward, so its transpose maps world -> camera


class Renderer:
    def __init__(self, g, near=0.1, far=400.0):
        self.g = g
        self.near, self.far = near, far
        self.cov = cov3d(g)
        self.max_contrib = torch.zeros(g["n"], device=DEV)
        self.alive = torch.ones(g["n"], dtype=torch.bool, device=DEV)

    @torch.no_grad()
    def render(self, eye, yaw, pitch, fov_v, width, height, image=False, tile=256,
               pair_budget=40_000_000):
        g = self.g
        basis = torch.tensor(camera_basis(yaw, pitch), dtype=torch.float32, device=DEV)
        eye_t = torch.tensor(eye, dtype=torch.float32, device=DEV)
        pc = (g["pos"] - eye_t) @ basis.T          # x right, y up, z forward
        z = pc[:, 2]
        keep = self.alive & (z > self.near) & (z < self.far)
        idx = keep.nonzero().squeeze(1)
        pc, z = pc[idx], z[idx]

        tan_v = math.tan(math.radians(fov_v) / 2)
        f = height / 2 / tan_v                     # focal in pixels (square pixels)
        tan_h = width / 2 / f
        lim_x, lim_y = 1.3 * tan_h, 1.3 * tan_v
        tx = (pc[:, 0] / z).clamp(-lim_x, lim_x) * z
        ty = (pc[:, 1] / z).clamp(-lim_y, lim_y) * z
        # Pixel y runs DOWN, so the y row of the Jacobian is negated. The covariance is
        # quadratic in J, so only the off-diagonal term notices, exactly as the flip should.
        J = torch.zeros(len(idx), 2, 3, device=DEV)
        J[:, 0, 0] = f / z
        J[:, 0, 2] = -f * tx / (z * z)
        J[:, 1, 1] = -f / z
        J[:, 1, 2] = f * ty / (z * z)
        T = J @ basis
        c2 = T @ self.cov[idx] @ T.transpose(1, 2)
        a, b, d = c2[:, 0, 0] + 0.3, c2[:, 0, 1], c2[:, 1, 1] + 0.3

        mid = 0.5 * (a + d)
        rad = torch.sqrt(((a - d) / 2) ** 2 + b * b)
        l1 = mid + rad
        l2 = (mid - rad).clamp_min(0.1)
        e1 = torch.stack([b, l1 - a], 1)
        e1 = e1 / e1.norm(dim=1, keepdim=True).clamp_min(1e-12)
        # a diagonal covariance leaves (b, l1 - a) at zero: fall back to the x axis
        e1 = torch.where(e1.norm(dim=1, keepdim=True) > 0.5, e1,
                         torch.tensor([1.0, 0.0], device=DEV).expand_as(e1))
        e2 = torch.stack([-e1[:, 1], e1[:, 0]], 1)
        s1, s2 = torch.sqrt(2 * l1), torch.sqrt(2 * l2)

        u = width / 2 + f * pc[:, 0] / z
        v = height / 2 - f * pc[:, 1] / z
        hx = 2 * (s1 * e1[:, 0].abs() + s2 * e2[:, 0].abs())
        hy = 2 * (s1 * e1[:, 1].abs() + s2 * e2[:, 1].abs())
        x0 = torch.floor(u - hx - 0.5).clamp(0, width).long()
        x1 = torch.ceil(u + hx - 0.5).clamp(-1, width - 1).long()
        y0 = torch.floor(v - hy - 0.5).clamp(0, height).long()
        y1 = torch.ceil(v + hy - 0.5).clamp(-1, height - 1).long()
        onscreen = (x1 >= x0) & (y1 >= y0)

        order = torch.argsort(z)                   # front to back
        rank = torch.empty_like(order)
        rank[order] = torch.arange(len(order), device=DEV)

        col = g["color"][idx]
        op = g["opacity"][idx]
        img = torch.zeros(height, width, 3, device=DEV) if image else None
        contrib = torch.zeros(len(idx), device=DEV)
        stats = {"fragments": 0, "pairs": 0}

        def do_tile(tx0, ty0, tx1, ty1):
            sel = onscreen & (x0 <= tx1) & (x1 >= tx0) & (y0 <= ty1) & (y1 >= ty0)
            gi = sel.nonzero().squeeze(1)
            if len(gi) == 0:
                return
            bx0, bx1 = x0[gi].clamp_min(tx0), x1[gi].clamp_max(tx1)
            by0, by1 = y0[gi].clamp_min(ty0), y1[gi].clamp_max(ty1)
            bw, bh = bx1 - bx0 + 1, by1 - by0 + 1
            cnt = bw * bh
            total = int(cnt.sum())
            if total > pair_budget and (tx1 - tx0 > 16 or ty1 - ty0 > 16):
                mx, my = (tx0 + tx1) // 2, (ty0 + ty1) // 2
                for q in ((tx0, ty0, mx, my), (mx + 1, ty0, tx1, my),
                          (tx0, my + 1, mx, ty1), (mx + 1, my + 1, tx1, ty1)):
                    if q[0] <= q[2] and q[1] <= q[3]:
                        do_tile(*q)
                return
            stats["pairs"] += total
            pg = torch.repeat_interleave(gi, cnt)
            start = torch.cumsum(cnt, 0) - cnt
            local = torch.arange(total, device=DEV) - torch.repeat_interleave(start, cnt)
            bwr = torch.repeat_interleave(bw, cnt)
            px = torch.repeat_interleave(bx0, cnt) + local % bwr
            py = torch.repeat_interleave(by0, cnt) + local // bwr
            dx = px.float() + 0.5 - u[pg]
            dy = py.float() + 0.5 - v[pg]
            q1 = (dx * e1[pg, 0] + dy * e1[pg, 1]) / s1[pg]
            q2 = (dx * e2[pg, 0] + dy * e2[pg, 1]) / s2[pg]
            inside = (q1.abs() <= 2) & (q2.abs() <= 2)
            alpha = (torch.exp(-(q1 * q1 + q2 * q2)) * op[pg]).clamp(max=1.0)
            ok = inside & (alpha >= 1.0 / 255.0)
            pg, px, py, alpha = pg[ok], px[ok], py[ok], alpha[ok]
            if len(pg) == 0:
                return
            stats["fragments"] += len(pg)
            pix = py.long() * width + px.long()
            key = pix * len(idx) + rank[pg]
            key, perm = torch.sort(key)
            pg, alpha, pix = pg[perm], alpha[perm], pix[perm]
            # exclusive transmittance per pixel: T = prod(1 - alpha) of everything in front
            la = torch.log1p(-alpha.clamp(max=1 - 1e-7).double())
            cs = torch.cumsum(la, 0)
            first = torch.ones_like(pix, dtype=torch.bool)
            first[1:] = pix[1:] != pix[:-1]
            seg_start = torch.cummax(torch.where(first, torch.arange(len(pix), device=DEV),
                                                 torch.zeros_like(pix)), 0).values
            before = cs - la - (cs[seg_start] - la[seg_start])
            w = (alpha.double() * torch.exp(before)).float()
            contrib.scatter_reduce_(0, pg, w, reduce="amax")
            if img is not None:
                flat = img.view(-1, 3)
                flat.index_add_(0, pix, col[pg] * w[:, None])

        for ty in range(0, height, tile):
            for tx in range(0, width, tile):
                do_tile(tx, ty, min(tx + tile, width) - 1, min(ty + tile, height) - 1)

        full = self.max_contrib.new_zeros(g["n"])
        full[idx] = contrib
        torch.maximum(self.max_contrib, full, out=self.max_contrib)
        stats["splats_in_view"] = int((contrib > 0).sum())
        return (img.clamp(0, 1) if img is not None else None), stats
