using UnityEngine;
using UnityEngine.InputSystem;

namespace MuseXR.Interaction
{
    /// <summary>Something A confirms and B redoes: a slot station, the card choice, the companions' turns.</summary>
    public interface IConfirmable
    {
        bool Confirm();
        bool Redo();
    }

    /// <summary>
    /// Her A and B (§3.1: "A confirms or advances. B undoes, redoes or closes"). One pair of actions
    /// for the whole app, routed to whichever choice was made last, so A never confirms two things
    /// at once. Right controller A / B; Enter / Backspace in the Editor.
    /// </summary>
    public sealed class ConfirmInput : MonoBehaviour
    {
        /// <summary>
        /// Who A and B go to: the most recent holder still alive and switched on. Holders stack, so when one lets go
        /// (a round of the masters ends, a card closes) A returns to whoever held it before, instead of to nobody
        /// (Saul, 5 Oct: A on a lantern stopped opening the Palace once a master had spoken about a painting).
        /// </summary>
        public static IConfirmable Focus
        {
            get
            {
                for (var i = Held.Count - 1; i >= 0; i--)
                {
                    var h = Held[i];
                    if (Alive(h)) return h;
                    Held.RemoveAt(i);
                }
                return null;
            }
        }

        static readonly System.Collections.Generic.List<IConfirmable> Held = new System.Collections.Generic.List<IConfirmable>();

        /// <summary>Not a destroyed or switched-off component (a finished chapter's station never takes A back).</summary>
        static bool Alive(IConfirmable h) =>
            h != null && (!(h is Object o) || (o != null && (!(o is Behaviour b) || b.isActiveAndEnabled)));

        /// <summary>Every press of A or B: which button, what it went to, and whether it did anything.</summary>
        public static event System.Action<string, string, bool> Pressed;

        static ConfirmInput _instance;
        InputAction _a, _b;

        /// <summary>Route A and B to <paramref name="target"/> from now on.</summary>
        public static void Take(IConfirmable target)
        {
            if (target == null) return;
            Held.Remove(target);
            Held.Add(target);
            if (_instance != null || !Application.isPlaying) return;   // the listener is a Play thing (EditMode tests route by hand)
            var go = new GameObject("Confirm Input") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ConfirmInput>();
        }

        /// <summary>Stop routing to <paramref name="target"/>: A goes back to whoever held it before.</summary>
        public static void Drop(IConfirmable target) => Held.Remove(target);

        /// <summary>Nobody holds A or B: a fresh journey (Start again reloads the scene, and the old scene's holders -
        /// the Gate's company among them - stayed in the list beneath whatever the new one took).</summary>
        public static void Clear() => Held.Clear();

        /// <summary>Press A or B from code: the test harness.</summary>
        public static bool PressA() => Press("A", f => f.Confirm());
        public static bool PressB() => Press("B", f => f.Redo());

        static bool Press(string button, System.Func<IConfirmable, bool> act)
        {
            var target = Focus;
            var ok = target != null && act(target);
            Pressed?.Invoke(button, target != null ? target.GetType().Name : "nothing", ok);
            return ok;
        }

        void Awake()
        {
            _a = new InputAction("confirm-a", InputActionType.Button);
            _a.AddBinding("<XRController>{RightHand}/primaryButton");
            _a.AddBinding("<Keyboard>/enter");
            _b = new InputAction("redo-b", InputActionType.Button);
            _b.AddBinding("<XRController>{RightHand}/secondaryButton");
            _b.AddBinding("<Keyboard>/backspace");
            _a.Enable(); _b.Enable();
        }

        void OnDestroy()
        {
            _a?.Dispose(); _b?.Dispose();
            if (_instance == this) _instance = null;
        }

        void Update()
        {
            if (_a.WasPressedThisFrame()) PressA();
            if (_b.WasPressedThisFrame()) PressB();
        }
    }
}
