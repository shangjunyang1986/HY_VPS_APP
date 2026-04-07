using UnityEngine;
using UnityEngine.UI;

namespace VPS
{
    /// <summary>
    /// Displays EasyAR camera feed as AR background.
    /// Attach to a Canvas GameObject with a full-screen RawImage child.
    /// </summary>
    public class CameraBackground : MonoBehaviour
    {
        [SerializeField] private EasyARTracker tracker;
        [SerializeField] private RawImage rawImage;
        [SerializeField] private bool flipVertical = true;

        private void Start()
        {
            if (tracker == null)
                tracker = FindAnyObjectByType<EasyARTracker>();
        }

        private void Update()
        {
            if (tracker == null || rawImage == null) return;
            var tex = tracker.BackgroundTexture;
            if (tex != null)
            {
                rawImage.texture = tex;
                // EasyAR camera images often need vertical flip for correct orientation
                rawImage.uvRect = flipVertical
                    ? new Rect(0f, 1f, 1f, -1f)
                    : new Rect(0f, 0f, 1f, 1f);
            }
        }
    }
}
