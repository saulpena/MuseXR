using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace MusePico.Grab
{
    /// <summary>How a ray grab takes hold of the object.</summary>
    public enum GrabReach
    {
        /// <summary>It stays where the ray struck it and moves on the end of the ray.</summary>
        AtRayEnd,
        /// <summary>
        /// Held where the ray struck it, then reeled in toward the controller at
        /// <see cref="Grabbable.ReelSpeed"/> for as long as the trigger stays down — let go and it
        /// stops where it is. Reach into it and it is simply in your hand.
        /// </summary>
        PullToHand,
    }

    /// <summary>
    /// A painting or prop the visitor can pick up with a controller — by the ray, or by reaching
    /// into it — carry, and resize by pulling it between two hands. OpenXR + XRI only, so the
    /// same component works on PICO and Quest: nothing here knows which vendor's controller is
    /// holding it.
    ///
    /// A quick trigger tap is still a click — <see cref="Tapped"/> fires and the object snaps back
    /// to where it was, so pointing at a work never nudges it. See <see cref="GrabGesture"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Grabbable : MonoBehaviour
    {
        /// <summary>
        /// Two-hand scaling limits, relative to the size it was placed at. x8 takes a 0.85 m
        /// painting to about 6.8 m: tall enough to stand on the floor and walk into.
        /// </summary>
        public const float MinScaleRatio = 0.25f;
        public const float MaxScaleRatio = 8f;

        /// <summary>
        /// Measured in Play Mode on the real Near-Far interactors, with nothing moving between
        /// reads. <c>TwoHandedAverage</c> SNAPS the object 90 degrees the instant the second hand
        /// joins (yaw 18 -> 288): it re-aims the object down the line between the hands and does
        /// not keep the offset it had. <c>FirstHandOnly</c> keeps the pose but can never scale —
        /// XRI holds the hand-to-hand distance at its starting value in that mode. This one is
        /// relative to where the hands were when the second grabbed: yaw 36.0 before and after,
        /// and x2.61 from spreading the rays.
        /// </summary>
        public const XRGeneralGrabTransformer.TwoHandedRotationMode TwoHandRotation =
            XRGeneralGrabTransformer.TwoHandedRotationMode.FirstHandDirectedTowardsSecondHand;

        /// <summary>
        /// How fast a ray-held object comes to the hand while the trigger is held, m/s. The first
        /// version eased it in over 0.15 s regardless of distance, which in the headset read as the
        /// object flying at your face. A steady speed makes the pull something you control: hold
        /// to bring it closer, let go to leave it there. A painting 4 m out takes about 4 s.
        /// </summary>
        public const float ReelSpeed = 1f;

        /// <summary>What a tap does. Null means a tap does nothing but restore the pose.</summary>
        public Action Tapped;

        /// <summary>Serialized so a scene keeps it: whether a one-hand ray grab reels in.</summary>
        [SerializeField] GrabReach m_Reach = GrabReach.AtRayEnd;
        public GrabReach Reach => m_Reach;

        XRGrabInteractable _grab;
        float _pressedAt;
        int _mostHands;
        Transform _hand;
        Vector3 _handFrom;
        Quaternion _handFromRotation;
        Vector3 _position;
        Quaternion _rotation;
        Vector3 _scale;
        bool _clearOfBody;

        public XRGrabInteractable Interactable => _grab != null ? _grab : _grab = GetComponent<XRGrabInteractable>();

        /// <summary>
        /// Make <paramref name="root"/> grabbable. Works at runtime and in the Editor (the scene then
        /// serializes the result). Colliders are handed over explicitly and before the interactable
        /// registers: left to itself XRBaseInteractable collects them in Awake and skips triggers.
        /// </summary>
        public static Grabbable Make(GameObject root, Action onTap = null, GrabReach reach = GrabReach.AtRayEnd,
                                     params Collider[] colliders)
        {
            var existing = root.GetComponent<Grabbable>();
            if (existing != null)
            {
                existing.Tapped = onTap;
                existing.m_Reach = reach;
                Configure(existing.Interactable, reach);
                var list = existing.Interactable.colliders;
                for (var i = 0; i < list.Count; i++) list[i] = Solid(list[i]);
                return existing;
            }

            var wasActive = root.activeSelf;
            root.SetActive(false);      // so Awake/OnEnable run after everything below is set

            // Never falls or gets pushed: kinematic, no gravity, and it stays where it is let go
            // of rather than being thrown.
            var body = root.GetComponent<Rigidbody>();
            if (body == null) body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var transformer = root.AddComponent<XRGeneralGrabTransformer>();
            transformer.allowOneHandedScaling = false;  // the thumbsticks already turn and move
            transformer.allowTwoHandedScaling = true;
            transformer.allowTwoHandedRotation = TwoHandRotation;
            transformer.clampScaling = true;
            transformer.minimumScaleRatio = MinScaleRatio;
            transformer.maximumScaleRatio = MaxScaleRatio;

            var grab = root.AddComponent<XRGrabInteractable>();
            grab.selectMode = InteractableSelectMode.Multiple;          // the second hand joins, not steals
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;
            grab.trackScale = true;
            grab.addDefaultGrabTransformers = false;
            // Held where it was touched, not by its centre, so it does not jump on grab.
            grab.useDynamicAttach = true;
            grab.matchAttachPosition = true;
            grab.matchAttachRotation = true;
            // Stay under the wall: the journey reads its works as the wall's children.
            grab.unparentTransformOnGrab = false;
            grab.retainTransformParent = true;
            Configure(grab, reach);
            if (colliders != null && colliders.Length > 0)
            {
                grab.colliders.Clear();
                foreach (var c in colliders) if (c != null) grab.colliders.Add(Solid(c));
            }

            var grabbable = root.AddComponent<Grabbable>();
            grabbable._grab = grab;
            grabbable.Tapped = onTap;
            grabbable.m_Reach = reach;

            root.SetActive(wasActive);
            return grabbable;
        }

        /// <summary>
        /// A collider XRI can take hold of. When a grab pulls an object in, or a hand reaches into
        /// one, XRI places the grip with <c>Collider.ClosestPoint</c> — which a non-convex
        /// MeshCollider does not support: it hands the query point straight back. A painting is a
        /// Quad, whose primitive collider is exactly that, so a pulled painting kept its full 3.65 m
        /// offset and never arrived (measured; the box-collided props came to hand). The mesh
        /// collider is swapped for a box of the same bounds, a centimetre thick if the mesh is flat.
        /// </summary>
        public static Collider Solid(Collider collider)
        {
            if (!(collider is MeshCollider mesh) || mesh.convex || mesh.sharedMesh == null) return collider;
            var go = mesh.gameObject;
            var bounds = mesh.sharedMesh.bounds;
            var trigger = mesh.isTrigger;
            DestroyImmediate(mesh);
            var box = go.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = new Vector3(bounds.size.x, bounds.size.y, Mathf.Max(bounds.size.z, 0.01f));
            box.isTrigger = trigger;
            return box;
        }

        /// <summary>
        /// How much of the change in hand spacing becomes change in size. XRI's default is 0.25,
        /// which suits a far grab — the ray ends swing metres for a small wrist turn — but held in
        /// the hands it is far too weak: measured, spreading the hands 0.81 -> 1.41 m (x1.74) grew a
        /// painting only x1.13. At 1 the object's size follows the hands, which is what reaching
        /// out to stretch something feels like it should do.
        /// </summary>
        public const float CloseScaleMultiplier = 1f;
        public const float FarScaleMultiplier = 0.25f;

        static void Configure(XRGrabInteractable grab, GrabReach reach)
        {
            var transformer = grab.GetComponent<XRGeneralGrabTransformer>();
            // Both reaches take hold at the ray end; PullToHand then reels it in (Update).
            grab.farAttachMode = InteractableFarAttachMode.Far;
            grab.attachEaseInTime = 0f;
            if (transformer != null)
            {
                transformer.minimumScaleRatio = MinScaleRatio;
                transformer.maximumScaleRatio = MaxScaleRatio;
            }
            if (reach == GrabReach.PullToHand)
            {
                if (transformer != null) transformer.scaleMultiplier = CloseScaleMultiplier;
            }
            else
            {
                if (transformer != null) transformer.scaleMultiplier = FarScaleMultiplier;
            }
        }

        void OnEnable()
        {
            Interactable.selectEntered.AddListener(OnSelectEntered);
            Interactable.selectExited.AddListener(OnSelectExited);
        }

        void OnDisable()
        {
            if (_grab == null) return;
            _grab.selectEntered.RemoveListener(OnSelectEntered);
            _grab.selectExited.RemoveListener(OnSelectExited);
        }

        /// <summary>
        /// Held at arm's length, the object sits inside the space the visitor's CharacterController
        /// walks into, and a kinematic collider there stops them dead. Nothing grabbable should
        /// block the person carrying it, so the pair is told to ignore each other, once.
        /// </summary>
        void ClearOfBody()
        {
            if (_clearOfBody) return;
            _clearOfBody = true;
            var body = FindFirstObjectByType<CharacterController>();
            if (body == null) return;
            foreach (var c in _grab.colliders) if (c != null) Physics.IgnoreCollision(body, c, true);
        }

        /// <summary>
        /// Reel a one-hand ray grab in toward the controller. The grip point is the Near-Far
        /// interactor's attach anchor, sitting on the ray at the hit; walking it toward the hand
        /// walks the object with it. Only for ONE hand: with two, shortening one grip would shorten
        /// the hand-to-hand bar the scaler reads, and the object would shrink as it came.
        /// </summary>
        void Update()
        {
            if (m_Reach != GrabReach.PullToHand || _grab == null || _grab.interactorsSelecting.Count != 1) return;
            if (!(_grab.interactorsSelecting[0] is UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor nearFar)) return;
            var attach = nearFar.interactionAttachController;
            if (attach == null || !attach.hasOffset || attach.transformToFollow == null) return;

            var hand = attach.transformToFollow.position;
            var grip = nearFar.GetAttachTransform(_grab).position;
            var distance = Vector3.Distance(hand, grip);
            var next = GrabGesture.ReelStep(distance, ReelSpeed, Time.deltaTime);
            if (next <= 0.01f) attach.ResetOffset();                     // arrived: held in the hand
            else attach.MoveTo(hand + (grip - hand) * (next / distance));
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            ClearOfBody();
            var hands = _grab.interactorsSelecting.Count;
            if (hands == 1)
            {
                _pressedAt = Time.unscaledTime;
                _mostHands = 1;
                _hand = args.interactorObject.transform;
                _handFrom = _hand.position;
                _handFromRotation = _hand.rotation;
                _position = transform.position;
                _rotation = transform.rotation;
                _scale = transform.localScale;
            }
            _mostHands = Mathf.Max(_mostHands, hands);
            Debug.Log("[Grab] " + name + " held by " + hands + (hands == 1 ? " hand" : " hands"));
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            if (_grab.interactorsSelecting.Count > 0) return;   // still in the other hand

            var travel = _hand != null ? Vector3.Distance(_hand.position, _handFrom) : float.MaxValue;
            var turn = _hand != null ? Quaternion.Angle(_hand.rotation, _handFromRotation) : float.MaxValue;
            var release = GrabGesture.Classify(Time.unscaledTime - _pressedAt, travel, turn, _mostHands);
            _hand = null;

            if (release != GrabRelease.Tap || args.isCanceled)
            {
                Debug.Log("[Grab] " + name + " put down at " + transform.position.ToString("F2") +
                          ", scale x" + (transform.localScale.y / Mathf.Max(_scale.y, 1e-4f)).ToString("F2"));
                return;
            }

            // A click: undo whatever the ray's wobble (or the pull) did during it, then do what a
            // click does.
            transform.SetPositionAndRotation(_position, _rotation);
            transform.localScale = _scale;
            Tapped?.Invoke();
        }
    }
}
