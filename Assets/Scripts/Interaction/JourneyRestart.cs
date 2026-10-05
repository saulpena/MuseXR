using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her "Start again" (4.5): the entry arch behind the visitor in Your world. Walking through it fades to black and
    /// begins the journey again at the Gate - a fresh record, the scene reloaded, so nothing of the last walk is left
    /// standing. Statics that outlive a scene load are cleared here, in one place.
    /// </summary>
    public sealed class JourneyRestart : MonoBehaviour
    {
        public const float FadeSeconds = 0.6f;
        static bool _running;

        public static void Begin()
        {
            if (_running) return;
            _running = true;
            var go = new GameObject("Journey Restart");
            DontDestroyOnLoad(go);
            go.AddComponent<JourneyRestart>().StartCoroutine(go.GetComponent<JourneyRestart>().Run());
        }

        /// <summary>Everything static the journey carries, back to a first visit.</summary>
        public static void ClearStatics()
        {
            JourneyMemory.Reset();
            CompassBrief.Reset();
            ArtworkCard.Hushed = false;
        }

        IEnumerator Run()
        {
            Debug.Log("[Journey] start again: back to the Gate");
            var quad = Black();
            for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime) { Alpha(quad, t / FadeSeconds); Follow(quad); yield return null; }
            Alpha(quad, 1f);
            ClearStatics();
            var scene = SceneManager.GetActiveScene();
            AsyncOperation load;
#if UNITY_EDITOR
            // Play Mode: the open scene need not be in the build list.
            load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scene.path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            load = SceneManager.LoadSceneAsync(scene.buildIndex);
#endif
            while (load != null && !load.isDone) { Follow(quad); yield return null; }
            for (var i = 0; i < 10; i++) { Follow(quad); yield return null; }   // the Gate's first frames, under black
            for (float t = 0f; t < FadeSeconds; t += Time.unscaledDeltaTime) { Alpha(quad, 1f - t / FadeSeconds); Follow(quad); yield return null; }
            _running = false;
            Destroy(gameObject);
        }

        Renderer Black()
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Restart fade";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 5000;
            m.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));
            var r = q.GetComponent<Renderer>(); r.sharedMaterial = m;
            Follow(r);
            return r;
        }

        static void Alpha(Renderer r, float a) { if (r != null) r.sharedMaterial.SetColor("_BaseColor", new Color(0f, 0f, 0f, Mathf.Clamp01(a))); }

        /// <summary>Just in front of the eye, filling it: a flat thing reads with its +Z away from the viewer.</summary>
        static void Follow(Renderer r)
        {
            var cam = Camera.main;
            if (r == null || cam == null) return;
            r.transform.SetPositionAndRotation(cam.transform.position + cam.transform.forward * 0.15f, cam.transform.rotation);
            r.transform.localScale = Vector3.one * 2f;
        }
    }
}
