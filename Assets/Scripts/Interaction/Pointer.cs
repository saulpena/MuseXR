using System;
using System.Collections.Generic;
using MuseXR.Slots;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>Anything the trigger's ray can point at: a standee, a lantern, a card, an artwork.</summary>
    public interface IPointable
    {
        void HoverEnter(Pointer pointer);
        void HoverExit(Pointer pointer);
        void Select(Pointer pointer);
    }

    /// <summary>
    /// A plain pointable with C# events, for objects that just need "pointed at" and "chosen". Hover
    /// state is counted across hands, so two rays on one object hover it once.
    /// </summary>
    public sealed class Pointable : MonoBehaviour, IPointable
    {
        int _hovers;

        public string Id { get; set; } = string.Empty;
        /// <summary>The name shown over it while the ray is on it (<see cref="HoverCue"/>); none, no brackets.</summary>
        public string Label { get; set; }
        public bool Hovered => _hovers > 0;
        /// <summary>Off: rays pass over it without hovering or selecting (a kept choice, a stage not yet open).</summary>
        public bool Interactive { get; set; } = true;

        public event Action<Pointable> Hovering;
        public event Action<Pointable> Unhovered;
        public event Action<Pointable, Pointer> Selected;

        /// <summary>Make <paramref name="go"/> pointable. Adds a collider fitted to its renderers if it has none.</summary>
        /// <summary>Wider than this (m) a piece is pointed at by its own surfaces, not one box round it.</summary>
        public const float LargePiece = 3f;
        /// <summary>A separate part of a large mesh smaller than this (m, its longest side) gets no box of its own (a petal).</summary>
        public const float MinIsland = 0.4f;
        public const int MaxIslands = 24;

        public static Pointable Make(GameObject go, string id)
        {
            var p = go.GetComponent<Pointable>();
            if (p == null) p = go.AddComponent<Pointable>();   // not ??: Unity's fake null defeats it
            p.Id = id;
            if (go.GetComponentInChildren<Collider>() == null)
            {
                var rs = go.GetComponentsInChildren<Renderer>();
                // A piece big enough to stand inside (the five Buddhas: one mesh 10 m across) gets colliders on its own
                // surfaces. One box round it all contained the visitor, and a ray that starts inside a box never hits it:
                // the group answered a click only from behind (Saul, 5 Oct).
                if (rs.Length > 0)
                {
                    var all = rs[0].bounds; foreach (var r in rs) all.Encapsulate(r.bounds);
                    if (Mathf.Max(all.size.x, all.size.z) > LargePiece)
                    {
                        // One box per separate figure in the mesh (the five Buddhas are one mesh of five islands): generous
                        // to aim at, never big enough to stand inside. Surfaces alone let the ray slip between the figures
                        // to the group behind (Saul, 5 Oct). An unreadable mesh falls back to its own surfaces.
                        var any = false;
                        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                        {
                            if (mf.sharedMesh == null) continue;
                            var islands = mf.sharedMesh.isReadable ? MeshIslands.Bounds(mf.sharedMesh, MinIsland, MaxIslands) : null;
                            if (islands == null || islands.Count == 0) { mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; any = true; continue; }
                            var scale = mf.transform.lossyScale;
                            var figures = 0;
                            foreach (var ib in islands)
                            {
                                // A part as wide as a platform (the five Buddhas' lotus terrace) is not a figure: its box
                                // caught the ray a metre from the visitor, in front of everything behind it (5 Oct).
                                if (Mathf.Max(Mathf.Abs(ib.size.x * scale.x), Mathf.Abs(ib.size.z * scale.z)) > LargePiece) continue;
                                var fig = new GameObject("Hit figure").AddComponent<BoxCollider>();
                                fig.transform.SetParent(mf.transform, false);
                                fig.center = ib.center; fig.size = ib.size; fig.isTrigger = true;
                                any = true; figures++;
                            }
                            if (figures == 0 && mf.sharedMesh.isReadable)
                            {
                                // One joined mesh (the five Buddhas are fused to their terrace): find the figures standing
                                // on it instead - tall geometry clustered on the floor plan - each boxed a quarter wider.
                                foreach (var fb in MeshIslands.Figures(mf.sharedMesh, 0.45f, 0.25f, 64, 4, MaxIslands))
                                {
                                    var fig = new GameObject("Hit figure").AddComponent<BoxCollider>();
                                    fig.transform.SetParent(mf.transform, false);
                                    fig.center = fb.center; fig.size = new Vector3(fb.size.x * 1.25f, fb.size.y, fb.size.z * 1.25f); fig.isTrigger = true;
                                    any = true; figures++;
                                }
                            }
                            if (figures == 0) { mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; any = true; }
                        }
                        if (any) return p;
                    }
                }
                var box = go.AddComponent<BoxCollider>();
                if (rs.Length > 0)
                {
                    var b = rs[0].bounds;
                    foreach (var r in rs) b.Encapsulate(r.bounds);
                    box.center = go.transform.InverseTransformPoint(b.center);
                    var s = go.transform.lossyScale;
                    box.size = new Vector3(b.size.x / Mathf.Max(1e-4f, s.x), b.size.y / Mathf.Max(1e-4f, s.y), Mathf.Max(0.02f, b.size.z / Mathf.Max(1e-4f, s.z)));
                }
                box.isTrigger = true;
            }
            return p;
        }

        public void HoverEnter(Pointer pointer)
        {
            if (!Interactive) return;
            if (_hovers++ == 0) Hovering?.Invoke(this);
        }

        public void HoverExit(Pointer pointer)
        {
            if (_hovers == 0) return;
            if (--_hovers == 0) Unhovered?.Invoke(this);
        }

        public void Select(Pointer pointer)
        {
            if (Interactive) Selected?.Invoke(this, pointer);
        }

        void OnDisable() { if (_hovers > 0) { _hovers = 0; Unhovered?.Invoke(this); } }
    }

    /// <summary>
    /// One hand's trigger ray (her "Trigger ... selects"). Each frame it finds the first pointable
    /// along the controller's forward, hovers it (with a light tick in the hand) and selects it on
    /// the trigger's press. It stands down while its hand holds something, so a carried piece is
    /// never also "pointed at". Objects carry <see cref="IPointable"/>, not an XRI interactable, so
    /// XRI's own Select (also the trigger) never fires on them twice.
    /// </summary>
    public sealed class Pointer : MonoBehaviour
    {
        public const float Reach = 8f;
        /// <summary>Closer to the hand than this, a hovered thing gets no hover cue.</summary>
        public const float HandReach = 0.5f;

        public IHandSource Source { get; private set; }
        public GripHand Grip { get; private set; }
        public IPointable Hovered { get; private set; }
        public Vector3 HitPoint { get; private set; }

        public static readonly List<Pointer> All = new List<Pointer>();

        readonly RaycastHit[] _hits = new RaycastHit[16];
        bool _wasTrigger;

        public static Pointer Attach(GameObject host, IHandSource source, GripHand grip)
        {
            var p = host.AddComponent<Pointer>();
            p.Source = source;
            p.Grip = grip;
            return p;
        }

        void OnEnable() => All.Add(this);

        void OnDisable()
        {
            All.Remove(this);
            SetHover(null);
        }

        void Update()
        {
            if (Source?.Aim == null) return;
            var busy = Grip != null && Grip.Held != null;
            SetHover(busy ? null : Find());
            var trigger = Source.Trigger;
            if (trigger && !_wasTrigger && Hovered != null) { _cue?.Flash(); Hovered.Select(this); }
            _wasTrigger = trigger;
            DrawLaser();
        }

        LineRenderer _laser;

        /// <summary>Draw the laser without a headset too (for checking it in the Editor).</summary>
        public static bool LaserAtDesk;

        /// <summary>
        /// Saul, 4 Oct: the laser stopped short of the masters (the XR toolkit's ray draws a fading "no valid target"
        /// line for anything that is not its own interactable). On a headset, while this pointer is on something it
        /// can point at, a thin line runs all the way from the hand to the hit.
        /// </summary>
        void DrawLaser()
        {
            var show = Hovered != null && (UnityEngine.XR.XRSettings.isDeviceActive || LaserAtDesk) && Source?.Aim != null;
            if (!show) { if (_laser != null && _laser.enabled) _laser.enabled = false; return; }
            if (_laser == null)
            {
                var go = new GameObject("Laser");
                go.transform.SetParent(transform, false);
                _laser = go.AddComponent<LineRenderer>();
                _laser.useWorldSpace = true; _laser.positionCount = 2;
                _laser.startWidth = 0.004f; _laser.endWidth = 0.0025f;
                var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                m.SetColor("_BaseColor", new Color(1f, 0.93f, 0.75f));
                _laser.sharedMaterial = m;
                _laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _laser.enabled = true;
            _laser.SetPosition(0, Source.Aim.position);
            _laser.SetPosition(1, HitPoint);
        }

        IPointable Find()
        {
            var aim = Source.Aim;
            var n = Physics.RaycastNonAlloc(aim.position, aim.forward, _hits, Reach, ~0, QueryTriggerInteraction.Collide);
            IPointable best = null;
            var nearest = float.MaxValue;
            for (var i = 0; i < n; i++)
            {
                var p = _hits[i].collider.GetComponentInParent<IPointable>();
                if (p == null || _hits[i].distance >= nearest) continue;
                if (p is Pointable plain && !plain.Interactive) continue;
                best = p; nearest = _hits[i].distance; HitPoint = _hits[i].point;
            }
            // A panel in front stops the ray: its buttons take the trigger (through the UI system), and whatever
            // stands behind it must not take it too. Pointing at a menu's option started an interaction with the
            // painting behind it (Saul, 5 Oct). A pointable that is itself on the panel is a hair nearer than it.
            if (best != null && UiDistance(new Ray(aim.position, aim.forward), nearest) < nearest - 0.02f) best = null;
            return best;
        }

        static readonly List<Canvas> _canvases = new List<Canvas>();
        static float _canvasesAt = -1f;

        /// <summary>
        /// How far along <paramref name="ray"/> the nearest visible, clickable piece of world-space UI lies (a card,
        /// a button, a panel's backing), or infinity. Only what the UI itself would take a click on counts:
        /// raycast targets showing on an active canvas.
        /// </summary>
        static float UiDistance(Ray ray, float within)
        {
            if (Time.unscaledTime - _canvasesAt > 0.15f || _canvasesAt < 0f)
            {
                _canvasesAt = Time.unscaledTime;
                _canvases.Clear();
                foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (c.isRootCanvas && c.renderMode == RenderMode.WorldSpace) _canvases.Add(c);
            }
            var best = float.PositiveInfinity;
            foreach (var canvas in _canvases)
            {
                if (canvas == null || !canvas.isActiveAndEnabled) continue;
                var graphics = UnityEngine.UI.GraphicRegistry.GetRaycastableGraphicsForCanvas(canvas);
                for (var i = 0; i < graphics.Count; i++)
                {
                    var g = graphics[i];
                    if (g == null || !g.isActiveAndEnabled || !g.raycastTarget || g.canvasRenderer.cull) continue;
                    if (g.canvasRenderer.GetInheritedAlpha() < 0.05f) continue;   // faded out: not there to click
                    var rt = g.rectTransform;
                    var plane = new Plane(rt.forward, rt.position);
                    if (!plane.Raycast(ray, out var d) || d <= 0f || d >= within || d >= best) continue;
                    var local = rt.InverseTransformPoint(ray.GetPoint(d));
                    if (rt.rect.Contains(new Vector2(local.x, local.y))) best = d;
                }
            }
            return best;
        }

        HoverCue _cue;

        void SetHover(IPointable next)
        {
            if (ReferenceEquals(next, Hovered)) return;
            Hovered?.HoverExit(this);
            Hovered = next;
            if (_cue == null) _cue = new GameObject("Hover cue").AddComponent<HoverCue>();
            // Nothing at the hand itself (a replica on the wrist, the satchel): a cue there drew round the controller's
            // tip and, drawn over everything, filled the view (Saul, 5 Oct, headset).
            _cue.Show(next != null && Vector3.Distance(HitPoint, Source != null && Source.Aim != null ? Source.Aim.position : HitPoint) < HandReach ? null : next);
            if (next == null) return;
            next.HoverEnter(this);
            Source?.Buzz(SlotRules.LightAmplitude * 0.6f, SlotRules.LightSeconds);
        }

        /// <summary>Whether any hand's ray is on <paramref name="target"/> right now.</summary>
        public static bool AnyOn(IPointable target)
        {
            foreach (var p in All) if (ReferenceEquals(p.Hovered, target)) return true;
            return false;
        }
    }
}
