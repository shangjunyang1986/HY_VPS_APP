using UnityEngine;
#if EASYAR_ENABLE
using easyar;
#endif

namespace VPS
{
    /// <summary>
    /// Wrapper around EasyAR Sense v4001 component-based API.
    /// Uses easyar.ARSession + MotionTrackerFrameSource for 6DoF VIO tracking.
    /// Provides camera image, pose, and intrinsics for VPS pipeline.
    ///
    /// Scene setup required:
    /// - GameObject with: easyar.ARSession, MotionTrackerFrameSource, EasyARController,
    ///   CameraImageRenderer (and other required components)
    /// - EasyAR License Key configured in Project Settings > EasyAR
    /// </summary>
    public class EasyARTracker : MonoBehaviour
    {
        [Header("EasyAR Settings")]
        [SerializeField] private int captureWidth = 1280;

#if EASYAR_ENABLE
        [Header("EasyAR References (auto-detected if null)")]
        [SerializeField] private easyar.ARSession easyARSession;
        [SerializeField] private MotionTrackerFrameSource motionTrackerSource;
#endif

        private bool _isInitialized;
        private bool _isTracking;
        private Texture2D _frameTexture;
        private bool _hasNewFrame;

        // Background display texture (updated every Unity frame for AR overlay)
        private Texture2D _bgTexture;
        private bool _bgDirty;
        public Texture2D BackgroundTexture => _bgTexture;

        // Intrinsics cache
        private float _fx, _fy, _cx, _cy;
        private int _imageWidth, _imageHeight;
        private bool _hasIntrinsics;

#if EASYAR_ENABLE
        // Cached InputFrame data from the event
        private InputFrame _latestFrame;
        private readonly object _frameLock = new object();
#endif

        public bool IsInitialized => _isInitialized;
        public bool IsTracking => _isTracking;

        /// <summary>
        /// Initialize EasyAR motion tracking. Returns true if setup succeeds.
        /// EasyAR components must exist in the scene.
        /// </summary>
        public bool Initialize()
        {
#if EASYAR_ENABLE
            if (_isInitialized) return true;

            try
            {
                // Auto-find EasyAR session if not assigned
                if (easyARSession == null)
                    easyARSession = FindAnyObjectByType<easyar.ARSession>();

                if (easyARSession == null)
                {
                    Debug.LogError("[EasyAR] No easyar.ARSession found in scene");
                    return false;
                }

                // Auto-find MotionTrackerFrameSource
                if (motionTrackerSource == null)
                    motionTrackerSource = FindAnyObjectByType<MotionTrackerFrameSource>();

                if (motionTrackerSource == null)
                {
                    Debug.LogError("[EasyAR] No MotionTrackerFrameSource found in scene");
                    return false;
                }

                // Subscribe to frame update event
                easyARSession.InputFrameUpdate += OnInputFrameUpdate;

                // Start the EasyAR session (AutoStart is disabled to avoid conflict with ARCore)
                easyARSession.StartSession();

                _isInitialized = true;
                Debug.LogError("[EasyAR] Motion tracking initialized successfully (component-based)");
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[EasyAR] Init failed: {e.Message}\n{e.StackTrace}");
                return false;
            }
#else
            Debug.LogWarning("[EasyAR] SDK not available (EASYAR_ENABLE not defined)");
            return false;
#endif
        }

#if EASYAR_ENABLE
        private void OnInputFrameUpdate(InputFrame inputFrame)
        {
            if (inputFrame == null) return;

            // Update tracking status
            if (inputFrame.hasSpatialInformation())
            {
                var status = inputFrame.trackingStatus();
                _isTracking = (status == MotionTrackingStatus.Tracking ||
                               status == MotionTrackingStatus.Limited);
            }
            else
            {
                _isTracking = false;
            }

            // Cache intrinsics
            if (inputFrame.hasCameraParameters())
            {
                using (var camParams = inputFrame.cameraParameters())
                {
                    if (camParams != null)
                    {
                        var focal = camParams.focalLength();
                        var principal = camParams.principalPoint();
                        var size = camParams.size();
                        _fx = focal.data_0;
                        _fy = focal.data_1;
                        _cx = principal.data_0;
                        _cy = principal.data_1;
                        _imageWidth = size.data_0;
                        _imageHeight = size.data_1;
                        _hasIntrinsics = true;
                    }
                }
            }

            // Cache the frame for image extraction (clone to keep reference)
            lock (_frameLock)
            {
                _latestFrame?.Dispose();
                _latestFrame = inputFrame.Clone();
                _hasNewFrame = true;
                _bgDirty = true;
            }
        }
#endif

        // Update background texture every Unity frame for AR overlay display
        private void Update()
        {
#if EASYAR_ENABLE
            if (!_bgDirty || !_isInitialized) return;

            InputFrame frame = null;
            lock (_frameLock)
            {
                if (_latestFrame == null) return;
                frame = _latestFrame.Clone();
                _bgDirty = false;
            }

            try { CacheBackgroundFrame(frame); }
            catch (System.Exception e)
            {
                Debug.LogError($"[EasyAR] BG frame error: {e.Message}");
            }
            finally { frame.Dispose(); }
#endif
        }

#if EASYAR_ENABLE
        private void CacheBackgroundFrame(InputFrame inputFrame)
        {
            using (var image = inputFrame.image())
            {
                if (image == null) return;
                int srcW = image.width();
                int srcH = image.height();
                if (srcW <= 0 || srcH <= 0) return;

                using (var buffer = image.buffer())
                {
                    if (buffer == null) return;
                    var format = image.format();
                    int bufSize = buffer.size();

                    // Use full resolution scaled to 640 width for smooth background
                    int dstW = Mathf.Min(srcW, 640) & ~1;
                    int dstH = ((int)(dstW * ((float)srcH / srcW))) & ~1;

                    byte[] rawBytes = new byte[bufSize];
                    buffer.copyToByteArray(0, rawBytes, 0, bufSize);

                    // Reuse CacheFrameImage pixel conversion logic
                    byte[] rgbaBytes = ConvertToRGBA(rawBytes, format, srcW, srcH);
                    if (rgbaBytes == null) return;

                    Texture2D srcTex = new Texture2D(srcW, srcH, TextureFormat.RGBA32, false);
                    srcTex.LoadRawTextureData(rgbaBytes);
                    srcTex.Apply();

                    if (_bgTexture == null || _bgTexture.width != dstW || _bgTexture.height != dstH)
                    {
                        if (_bgTexture != null) Object.Destroy(_bgTexture);
                        _bgTexture = new Texture2D(dstW, dstH, TextureFormat.RGBA32, false);
                    }

                    RenderTexture rt = RenderTexture.GetTemporary(dstW, dstH, 0);
                    Graphics.Blit(srcTex, rt);
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    _bgTexture.ReadPixels(new Rect(0, 0, dstW, dstH), 0, 0);
                    _bgTexture.Apply();
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    Object.Destroy(srcTex);
                }
            }
        }

        // Convert raw bytes to RGBA32 (shared logic)
        private static byte[] ConvertToRGBA(byte[] rawBytes, PixelFormat format, int srcW, int srcH)
        {
            if (format == PixelFormat.RGBA8888) return rawBytes;
            if (format == PixelFormat.RGB888)
            {
                var r = new byte[srcW * srcH * 4];
                for (int i = 0, j = 0; i < srcW * srcH; i++, j += 3)
                { int k = i * 4; r[k] = rawBytes[j]; r[k+1] = rawBytes[j+1]; r[k+2] = rawBytes[j+2]; r[k+3] = 255; }
                return r;
            }
            if (format == PixelFormat.BGR888)
            {
                var r = new byte[srcW * srcH * 4];
                for (int i = 0, j = 0; i < srcW * srcH; i++, j += 3)
                { int k = i * 4; r[k] = rawBytes[j+2]; r[k+1] = rawBytes[j+1]; r[k+2] = rawBytes[j]; r[k+3] = 255; }
                return r;
            }
            if (format == PixelFormat.BGRA8888)
            {
                var r = new byte[srcW * srcH * 4];
                for (int i = 0; i < srcW * srcH; i++)
                { int k = i * 4; r[k] = rawBytes[k+2]; r[k+1] = rawBytes[k+1]; r[k+2] = rawBytes[k]; r[k+3] = rawBytes[k+3]; }
                return r;
            }
            if (format == PixelFormat.Gray)
            {
                var r = new byte[srcW * srcH * 4];
                for (int i = 0; i < srcW * srcH; i++)
                { int k = i * 4; r[k] = r[k+1] = r[k+2] = rawBytes[i]; r[k+3] = 255; }
                return r;
            }
            if (format == PixelFormat.YUV_NV21 || format == PixelFormat.YUV_NV12)
            {
                var r = new byte[srcW * srcH * 4];
                int ySize = srcW * srcH;
                bool isNV21 = (format == PixelFormat.YUV_NV21);
                for (int row = 0; row < srcH; row++)
                    for (int col = 0; col < srcW; col++)
                    {
                        int yIdx = row * srcW + col;
                        int uvIdx = ySize + (row / 2) * srcW + (col & ~1);
                        byte y = rawBytes[yIdx], u, v;
                        if (isNV21) { v = rawBytes[uvIdx]; u = rawBytes[uvIdx + 1]; }
                        else { u = rawBytes[uvIdx]; v = rawBytes[uvIdx + 1]; }
                        int c = y - 16, d = u - 128, e = v - 128;
                        int k = yIdx * 4;
                        r[k] = (byte)Mathf.Clamp((298*c+409*e+128)>>8, 0, 255);
                        r[k+1] = (byte)Mathf.Clamp((298*c-100*d-208*e+128)>>8, 0, 255);
                        r[k+2] = (byte)Mathf.Clamp((298*c+516*d+128)>>8, 0, 255);
                        r[k+3] = 255;
                    }
                return r;
            }
            Debug.LogWarning($"[EasyAR BG] Unsupported format: {format}");
            return null;
        }
#endif

        /// <summary>
        /// Call every frame to update tracking state.
        /// With component-based API, EasyAR updates camera transform automatically.
        /// This method is kept for compatibility but does minimal work.
        /// </summary>
        public void UpdateTracking()
        {
            // No-op: EasyAR handles camera transform updates via its session.
            // Tracking status is updated in OnInputFrameUpdate callback.
        }

        /// <summary>
        /// Get the latest 6DoF pose.
        /// Since EasyAR controls camera transform directly (IsCameraUnderControl=true),
        /// we read from the ARSession's camera rather than extracting from InputFrame matrix.
        /// </summary>
        public bool TryGetPose(out Vector3 position, out Quaternion rotation)
        {
            if (!_isInitialized || !_isTracking)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }

#if EASYAR_ENABLE
            // EasyAR updates the camera transform automatically each frame.
            // Read from the session's camera directly.
            var cam = Camera.main;
            if (cam != null)
            {
                position = cam.transform.position;
                rotation = cam.transform.rotation;
                return true;
            }
#endif

            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        /// <summary>
        /// Get the latest camera frame as Texture2D, scaled to captureWidth.
        /// </summary>
public Texture2D TryGetCameraImage()
        {
#if EASYAR_ENABLE
            if (!_isInitialized)
            {
                Debug.LogWarning("[EasyAR] TryGetCameraImage: not initialized");
                return null;
            }
            if (!_hasNewFrame)
            {
                return null;
            }

            InputFrame frame = null;
            lock (_frameLock)
            {
                if (_latestFrame == null)
                {
                    Debug.LogWarning("[EasyAR] TryGetCameraImage: _latestFrame is null");
                    return null;
                }
                frame = _latestFrame.Clone();
                _hasNewFrame = false;
            }

            try
            {
                CacheFrameImage(frame);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[EasyAR] CacheFrameImage exception: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                frame.Dispose();
            }

            if (_frameTexture == null)
                Debug.LogWarning("[EasyAR] TryGetCameraImage: _frameTexture is null after CacheFrameImage");

            return _frameTexture;
#else
            return null;
#endif
        }

        /// <summary>
        /// Get camera intrinsics scaled to capture resolution.
        /// </summary>
        public bool TryGetIntrinsics(out float fx, out float fy, out float cx, out float cy)
        {
            if (!_hasIntrinsics || _imageWidth <= 0)
            {
                fx = fy = cx = cy = 0;
                return false;
            }

            // Scale intrinsics to captureWidth
            int dstW = Mathf.Min(_imageWidth, captureWidth);
            float scale = (float)dstW / _imageWidth;

            fx = _fx * scale;
            fy = _fy * scale;
            cx = _cx * scale;
            cy = _cy * scale;
            return true;
        }

#if EASYAR_ENABLE
        /// <summary>
        /// Extract image pixels from InputFrame and write to _frameTexture.
        /// </summary>
private void CacheFrameImage(InputFrame inputFrame)
        {
            using (var image = inputFrame.image())
            {
                if (image == null) return;

                int srcW = image.width();
                int srcH = image.height();
                if (srcW <= 0 || srcH <= 0) return;

                using (var buffer = image.buffer())
                {
                    if (buffer == null) return;

                    var format = image.format();
                    int bufSize = buffer.size();

                    int dstW = Mathf.Min(srcW, captureWidth) & ~1;
                    int dstH = ((int)(dstW * ((float)srcH / srcW))) & ~1;

                    byte[] rawBytes = new byte[bufSize];
                    buffer.copyToByteArray(0, rawBytes, 0, bufSize);

                    // Convert to RGBA32 pixels based on source format
                    byte[] rgbaBytes;

                    if (format == PixelFormat.RGBA8888)
                    {
                        rgbaBytes = rawBytes;
                    }
                    else if (format == PixelFormat.RGB888)
                    {
                        rgbaBytes = new byte[srcW * srcH * 4];
                        for (int i = 0, j = 0; i < srcW * srcH; i++, j += 3)
                        {
                            int k = i * 4;
                            rgbaBytes[k] = rawBytes[j];
                            rgbaBytes[k + 1] = rawBytes[j + 1];
                            rgbaBytes[k + 2] = rawBytes[j + 2];
                            rgbaBytes[k + 3] = 255;
                        }
                    }
                    else if (format == PixelFormat.BGR888)
                    {
                        rgbaBytes = new byte[srcW * srcH * 4];
                        for (int i = 0, j = 0; i < srcW * srcH; i++, j += 3)
                        {
                            int k = i * 4;
                            rgbaBytes[k] = rawBytes[j + 2];
                            rgbaBytes[k + 1] = rawBytes[j + 1];
                            rgbaBytes[k + 2] = rawBytes[j];
                            rgbaBytes[k + 3] = 255;
                        }
                    }
                    else if (format == PixelFormat.BGRA8888)
                    {
                        rgbaBytes = new byte[srcW * srcH * 4];
                        for (int i = 0; i < srcW * srcH; i++)
                        {
                            int k = i * 4;
                            rgbaBytes[k] = rawBytes[k + 2];
                            rgbaBytes[k + 1] = rawBytes[k + 1];
                            rgbaBytes[k + 2] = rawBytes[k];
                            rgbaBytes[k + 3] = rawBytes[k + 3];
                        }
                    }
                    else if (format == PixelFormat.Gray)
                    {
                        rgbaBytes = new byte[srcW * srcH * 4];
                        for (int i = 0; i < srcW * srcH; i++)
                        {
                            int k = i * 4;
                            rgbaBytes[k] = rawBytes[i];
                            rgbaBytes[k + 1] = rawBytes[i];
                            rgbaBytes[k + 2] = rawBytes[i];
                            rgbaBytes[k + 3] = 255;
                        }
                    }
                    else if (format == PixelFormat.YUV_NV21 || format == PixelFormat.YUV_NV12)
                    {
                        // NV21/NV12: Y plane (w*h) + interleaved UV plane (w*h/2)
                        rgbaBytes = new byte[srcW * srcH * 4];
                        int ySize = srcW * srcH;
                        bool isNV21 = (format == PixelFormat.YUV_NV21);
                        for (int row = 0; row < srcH; row++)
                        {
                            for (int col = 0; col < srcW; col++)
                            {
                                int yIdx = row * srcW + col;
                                int uvIdx = ySize + (row / 2) * srcW + (col & ~1);
                                byte y = rawBytes[yIdx];
                                byte u, v;
                                if (isNV21) { v = rawBytes[uvIdx]; u = rawBytes[uvIdx + 1]; }
                                else { u = rawBytes[uvIdx]; v = rawBytes[uvIdx + 1]; }

                                int c = y - 16;
                                int d = u - 128;
                                int e = v - 128;
                                int r = Mathf.Clamp((298 * c + 409 * e + 128) >> 8, 0, 255);
                                int g = Mathf.Clamp((298 * c - 100 * d - 208 * e + 128) >> 8, 0, 255);
                                int b = Mathf.Clamp((298 * c + 516 * d + 128) >> 8, 0, 255);

                                int k = yIdx * 4;
                                rgbaBytes[k] = (byte)r;
                                rgbaBytes[k + 1] = (byte)g;
                                rgbaBytes[k + 2] = (byte)b;
                                rgbaBytes[k + 3] = 255;
                            }
                        }
                    }
                    else if (format == PixelFormat.YUV_I420 || format == PixelFormat.YUV_YV12)
                    {
                        // I420/YV12: Y plane (w*h) + U plane (w*h/4) + V plane (w*h/4)
                        rgbaBytes = new byte[srcW * srcH * 4];
                        int ySize = srcW * srcH;
                        int uvSize = ySize / 4;
                        int halfW = srcW / 2;
                        bool isI420 = (format == PixelFormat.YUV_I420);
                        int uOff = isI420 ? ySize : ySize + uvSize;
                        int vOff = isI420 ? ySize + uvSize : ySize;
                        for (int row = 0; row < srcH; row++)
                        {
                            for (int col = 0; col < srcW; col++)
                            {
                                int yIdx = row * srcW + col;
                                int uvIdx = (row / 2) * halfW + (col / 2);
                                byte y = rawBytes[yIdx];
                                byte u = rawBytes[uOff + uvIdx];
                                byte v = rawBytes[vOff + uvIdx];

                                int c2 = y - 16;
                                int d2 = u - 128;
                                int e2 = v - 128;
                                int r = Mathf.Clamp((298 * c2 + 409 * e2 + 128) >> 8, 0, 255);
                                int g = Mathf.Clamp((298 * c2 - 100 * d2 - 208 * e2 + 128) >> 8, 0, 255);
                                int b = Mathf.Clamp((298 * c2 + 516 * d2 + 128) >> 8, 0, 255);

                                int k = yIdx * 4;
                                rgbaBytes[k] = (byte)r;
                                rgbaBytes[k + 1] = (byte)g;
                                rgbaBytes[k + 2] = (byte)b;
                                rgbaBytes[k + 3] = 255;
                            }
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[EasyAR] Unsupported pixel format: {format}");
                        return;
                    }

                    // Load RGBA data into source texture
                    Texture2D srcTex = new Texture2D(srcW, srcH, TextureFormat.RGBA32, false);
                    srcTex.LoadRawTextureData(rgbaBytes);
                    srcTex.Apply();

                    // Scale to target size
                    if (_frameTexture == null || _frameTexture.width != dstW || _frameTexture.height != dstH)
                    {
                        if (_frameTexture != null) Object.Destroy(_frameTexture);
                        _frameTexture = new Texture2D(dstW, dstH, TextureFormat.RGBA32, false);
                    }

                    RenderTexture rt = RenderTexture.GetTemporary(dstW, dstH, 0);
                    Graphics.Blit(srcTex, rt);
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    _frameTexture.ReadPixels(new Rect(0, 0, dstW, dstH), 0, 0);
                    _frameTexture.Apply();
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);

                    Object.Destroy(srcTex);
                }
            }
        }
#endif

        private void OnDestroy()
        {
#if EASYAR_ENABLE
            if (easyARSession != null)
                easyARSession.InputFrameUpdate -= OnInputFrameUpdate;

            lock (_frameLock)
            {
                _latestFrame?.Dispose();
                _latestFrame = null;
            }
#endif
            if (_frameTexture != null)
                Destroy(_frameTexture);
        }
    }
}
