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
        public static IConfirmable Focus { get; private set; }

        static ConfirmInput _instance;
        InputAction _a, _b;

        /// <summary>Route A and B to <paramref name="target"/> from now on.</summary>
        public static void Take(IConfirmable target)
        {
            Focus = target;
            if (_instance != null) return;
            var go = new GameObject("Confirm Input") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ConfirmInput>();
        }

        /// <summary>Stop routing to <paramref name="target"/>, if it has the focus.</summary>
        public static void Drop(IConfirmable target)
        {
            if (ReferenceEquals(Focus, target)) Focus = null;
        }

        /// <summary>Press A or B from code: the test harness.</summary>
        public static bool PressA() => Focus != null && Focus.Confirm();
        public static bool PressB() => Focus != null && Focus.Redo();

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
            if (Focus == null) return;
            if (_a.WasPressedThisFrame()) Focus.Confirm();
            if (_b.WasPressedThisFrame()) Focus.Redo();
        }
    }
}
