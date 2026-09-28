using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace MusePico.Journey
{
    /// <summary>
    /// Logs, from the headset, what each controller ray is actually on — to find out why the masters
    /// cannot be selected on the Quest while the panels can (27 Sep: a whole session produced panel
    /// presses and not one hover on a master, with all three click targets built).
    ///
    /// Twice a second, and only when something changed, per Near-Far interactor:
    ///   - what XRI says it is hovering, and
    ///   - what a plain physics ray along the controller's aim hits first (triggers included), and
    ///     at what distance — so "something invisible is in the way" and "the ray reaches the master
    ///     but XRI does not count it" read differently in logcat. Lines start "[RayDiag]".
    /// </summary>
    public sealed class RayDiagnostics : MonoBehaviour
    {
        readonly Dictionary<NearFarInteractor, string> _last = new Dictionary<NearFarInteractor, string>();
        float _next;

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;

            foreach (var nf in FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None))
            {
                var sb = new StringBuilder();
                sb.Append("hovered[");
                var hovered = nf.interactablesHovered;
                for (var i = 0; i < hovered.Count; i++)
                    sb.Append(i > 0 ? "," : "").Append(hovered[i].transform.parent != null
                        ? hovered[i].transform.parent.name + "/" + hovered[i].transform.name
                        : hovered[i].transform.name);
                sb.Append("] ");

                var aim = nf.farInteractionCaster != null ? nf.farInteractionCaster.effectiveCastOrigin : nf.transform;
                if (aim != null && Physics.Raycast(aim.position, aim.forward, out var hit, 30f, ~0, QueryTriggerInteraction.Collide))
                {
                    var c = hit.collider;
                    sb.Append("ray first hit ").Append(hit.distance.ToString("0.00")).Append(" m ")
                      .Append(c.GetType().Name).Append(c.isTrigger ? "(trigger) " : " ")
                      .Append(c.transform.parent != null ? c.transform.parent.name + "/" : "").Append(c.name)
                      .Append(" layer ").Append(LayerMask.LayerToName(c.gameObject.layer));
                }
                else sb.Append("ray hits nothing");

                var line = sb.ToString();
                if (_last.TryGetValue(nf, out var prev) && prev == line) continue;
                _last[nf] = line;
                Debug.Log("[RayDiag] " + nf.transform.parent.name + ": " + line);
            }
        }
    }
}
