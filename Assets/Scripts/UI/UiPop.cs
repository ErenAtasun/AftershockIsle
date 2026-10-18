using UnityEngine;

namespace AftershockIsle.UI
{
    /// <summary>Pencere her açılışta hafifçe büyüyerek belirir (gerçek zamanla; oyun duraklatılmışken de çalışır).</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class UiPop : MonoBehaviour
    {
        const float Duration = 0.14f;
        const float StartScale = 0.94f;

        public RectTransform Target;

        CanvasGroup group;
        float startedAt;
        bool done;

        void Awake() => group = GetComponent<CanvasGroup>();

        void OnEnable()
        {
            startedAt = Time.unscaledTime;
            done = false;
            Apply(0f);
        }

        void Update()
        {
            if (done) return;
            float t = Mathf.Clamp01((Time.unscaledTime - startedAt) / Duration);
            Apply(t);
            done = t >= 1f;
        }

        void Apply(float t)
        {
            float eased = 1f - (1f - t) * (1f - t);
            if (group != null) group.alpha = Mathf.Lerp(0.2f, 1f, eased);
            if (Target != null) Target.localScale = Vector3.one * Mathf.Lerp(StartScale, 1f, eased);
        }
    }
}
