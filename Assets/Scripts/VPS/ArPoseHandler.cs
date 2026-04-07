using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;

namespace VPS
{
    /// <summary>
    /// AR Pose Handler - VIO-dominant tracking with adaptive VPS requests.
    /// Every frame: read VIO → apply client fusion → check trigger → maybe send VPS.
    /// Three-tier tracking: ARCore → EasyAR → WebCam+Gyro fallback.
    /// </summary>
    public class ArPoseHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera arCamera;
        [SerializeField] private VpsClient vpsClient;

        [Header("AR References (auto-detected on device)")]
        [SerializeField] private ARCameraManager arCameraManager;
        [SerializeField] private XROrigin xrOrigin;

        [Header("EasyAR (fallback when ARCore unavailable)")]
        [SerializeField] private EasyARTracker easyARTracker;

        [Header("VPS Trigger Settings")]
        [SerializeField] private float normalInterval = 2.0f;
        [SerializeField] private float fastInterval = 1.0f;
        [SerializeField] private float driftThreshold = 0.5f;
        [SerializeField] private int maxConsecutiveFailures = 3;

        [Header("Fusion Settings")]
        [SerializeField] private float fusionSmoothSpeed = 8.0f;

        [Header("Capture Settings")]
        [SerializeField] private bool autoStart = true;
        [SerializeField] private int captureWidth = 640;

        [Header("Status (Read-Only)")]
        [SerializeField] private bool isCapturing = false;
        [SerializeField] private int frameCount = 0;
        [SerializeField] private string captureMode = "None";

        private Texture2D _captureTexture;
        private bool _isSending = false;
        private VpsTriggerController _trigger;
        private ClientFusion _fusion;

        // Cached VIO pose at the moment of VPS capture (for offset calculation)
        private Vector3 _captureVioPos;
        private Quaternion _captureVioRot;

        // EasyAR tracking mode
        private bool _useEasyAR;

        // WebCam fallback fields
        private WebCamTexture _webCam;
        private bool _useWebCam;
        private Texture2D _webCamScaled;

        // Intrinsics diagnostics
        private Intrinsics _lastIntrinsics;
        private string _intrinsicsSource = "N/A";

        // AR availability check state
        private bool _modeInitDone = false;
        private ARSession _arSession;

        // AR capture failure tracking — auto-fallback to WebCam
        private int _arCaptureFailCount = 0;
        private const int AR_CAPTURE_FAIL_THRESHOLD = 3;

        private bool UseARCamera => arCameraManager != null && arCameraManager.enabled
                                    && arCameraManager.subsystem != null
                                    && arCameraManager.subsystem.running;

        public bool IsCapturing => isCapturing;
        public int FrameCount => frameCount;
        public VpsTriggerController Trigger => _trigger;
        public ClientFusion Fusion => _fusion;
        public string CaptureMode => captureMode;
        public Intrinsics LastIntrinsics => _lastIntrinsics;
        public string IntrinsicsSource => _intrinsicsSource;

        private void Start()
        {
            if (arCamera == null) arCamera = Camera.main;
            if (vpsClient == null) vpsClient = FindAnyObjectByType<VpsClient>();
            if (arCameraManager == null && arCamera != null)
                arCameraManager = arCamera.GetComponent<ARCameraManager>();
            if (xrOrigin == null)
                xrOrigin = FindAnyObjectByType<XROrigin>();

            _arSession = FindAnyObjectByType<ARSession>();

            _trigger = new VpsTriggerController(new VpsTriggerController.Settings
            {
                normalInterval = normalInterval,
                fastInterval = fastInterval,
                driftThreshold = driftThreshold,
                maxConsecutiveFailures = maxConsecutiveFailures
            });

            _fusion = new ClientFusion(fusionSmoothSpeed);

            if (vpsClient != null)
            {
                vpsClient.OnFusedPoseReceived += OnFusedPoseReceived;
                vpsClient.OnTrackingStateChanged += OnTrackingStateChanged;
            }

            _useWebCam = false;

            if (Application.platform == RuntimePlatform.Android)
            {
                // On Android: disable AR components first, then check ARCore availability
                // This prevents crash on devices without ARCore support
                if (_arSession != null) _arSession.enabled = false;
                if (arCameraManager != null) arCameraManager.enabled = false;
                var arBg = arCamera?.GetComponent<ARCameraBackground>();
                if (arBg != null) arBg.enabled = false;

                Debug.Log("[AR Pose] Android detected, checking ARCore availability...");
                StartCoroutine(CheckARCoreAndInit());
            }
            else
            {
                // Editor mode
                _modeInitDone = true;
                captureMode = "Editor (RenderTexture)";
                Debug.Log("[AR Pose] Using Editor RenderTexture mode");
                if (autoStart) StartCapturing();
            }
        }

        private IEnumerator CheckARCoreAndInit()
        {
            yield return ARSession.CheckAvailability();

            var availability = ARSession.state;
            Debug.Log($"[AR Pose] ARSession.state after CheckAvailability: {availability}");

            if (availability == ARSessionState.NeedsInstall)
            {
                Debug.Log("[AR Pose] ARCore needs install, attempting...");
                yield return ARSession.Install();
                availability = ARSession.state;
                Debug.Log($"[AR Pose] ARSession.state after Install: {availability}");
            }

            if (availability == ARSessionState.Ready ||
                availability == ARSessionState.SessionInitializing ||
                availability == ARSessionState.SessionTracking)
            {
                // ARCore is available — enable AR components
                Debug.Log("[AR Pose] ARCore available! Enabling AR mode.");
                if (_arSession != null) _arSession.enabled = true;
                if (arCameraManager != null) arCameraManager.enabled = true;
                var arBg = arCamera?.GetComponent<ARCameraBackground>();
                if (arBg != null) arBg.enabled = true;

                captureMode = "AR (ARCore)";

                // Wait up to 3 seconds for ARCore session to actually start tracking
                // Some devices report Ready but fail during actual session creation
                float waitStart = Time.realtimeSinceStartup;
                bool sessionOk = false;
                while (Time.realtimeSinceStartup - waitStart < 3.0f)
                {
                    yield return null;
                    var state = ARSession.state;
                    if (state == ARSessionState.SessionTracking)
                    {
                        sessionOk = true;
                        Debug.Log("[AR Pose] ARCore session confirmed tracking!");
                        break;
                    }
                    // If session explicitly fails, bail out immediately
                    if (state == ARSessionState.Unsupported || state == ARSessionState.NeedsInstall)
                    {
                        Debug.LogWarning($"[AR Pose] ARCore session failed during init: {state}");
                        break;
                    }
                }

                if (!sessionOk)
                {
                    // Also check if camera subsystem is actually providing images
                    bool hasCpuImage = false;
                    if (arCameraManager != null && arCameraManager.subsystem != null && arCameraManager.subsystem.running)
                    {
                        if (arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage testImage))
                        {
                            hasCpuImage = true;
                            testImage.Dispose();
                        }
                    }

                    if (!hasCpuImage)
                    {
                        Debug.LogWarning("[AR Pose] ARCore session failed to provide camera images after 3s.");
                        DisableARComponents();
                        TryEasyAROrWebCam();
                    }
                    else
                    {
                        Debug.Log("[AR Pose] ARCore not fully tracking but camera images available, continuing in AR mode.");
                    }
                }
            }
            else
            {
                // ARCore not available — try EasyAR, then WebCam
                Debug.Log($"[AR Pose] ARCore not available (state={availability}).");
                DisableARComponents();
                TryEasyAROrWebCam();
            }

            _modeInitDone = true;
            if (autoStart) StartCapturing();
        }

        private void DisableARComponents()
        {
            if (_arSession != null)
                _arSession.enabled = false;
            if (arCameraManager != null)
                arCameraManager.enabled = false;
            var arBg = arCamera?.GetComponent<ARCameraBackground>();
            if (arBg != null)
                arBg.enabled = false;

            Debug.Log("[AR Pose] AR components disabled");
        }

        /// <summary>
        /// Try EasyAR motion tracking first; if unavailable, fall back to WebCam+Gyro.
        /// </summary>
        private void TryEasyAROrWebCam()
        {
            // Auto-find EasyARTracker if not assigned
            if (easyARTracker == null)
                easyARTracker = FindAnyObjectByType<EasyARTracker>();

            if (easyARTracker != null && easyARTracker.Initialize())
            {
                _useEasyAR = true;
                _useWebCam = false;
                captureMode = "EasyAR (VIO)";
                Debug.LogError("[AR Pose] EasyAR motion tracking initialized — using EasyAR mode");
            }
            else
            {
                Debug.LogWarning("[AR Pose] EasyAR not available, falling back to WebCam.");
                _useEasyAR = false;
                StartWebCam();
            }
        }

        private void StartWebCam()
        {
            // Find rear camera
            string deviceName = null;
            foreach (var device in WebCamTexture.devices)
            {
                if (!device.isFrontFacing)
                {
                    deviceName = device.name;
                    break;
                }
            }

            if (deviceName == null && WebCamTexture.devices.Length > 0)
                deviceName = WebCamTexture.devices[0].name;

            if (deviceName == null)
            {
                captureMode = "Editor (RenderTexture)";
                Debug.LogWarning("[AR Pose] No webcam found, falling back to Editor mode");
                return;
            }

            _webCam = new WebCamTexture(deviceName, 640, 480, 30);
            _webCam.Play();
            _useWebCam = true;
            captureMode = "WebCam (" + deviceName + ")";

            // Enable gyroscope for orientation tracking
            if (SystemInfo.supportsGyroscope)
            {
                Input.gyro.enabled = true;
                Debug.Log("[AR Pose] Gyroscope enabled for orientation tracking");
            }

            Debug.Log($"[AR Pose] WebCam mode started: {deviceName} ({_webCam.requestedWidth}x{_webCam.requestedHeight})");
        }

        public void StartCapturing()
        {
            isCapturing = true;
            frameCount = 0;
            _trigger?.Reset();
            _fusion?.Reset();

            if (vpsClient != null)
            {
                Debug.LogError($"[AR Pose] Calling StartSession on VpsClient (server={vpsClient.ServerUrl})");
                StartCoroutine(vpsClient.StartSession());
            }

            string mode = UseARCamera ? "AR mode" : (_useEasyAR ? "EasyAR mode" : (_useWebCam ? "WebCam mode" : "Editor mode"));
            Debug.LogError($"[AR Pose] Started capturing ({mode})");
        }

        public void StopCapturing()
        {
            isCapturing = false;

            if (vpsClient != null)
                StartCoroutine(vpsClient.EndSession());

            Debug.Log("[AR Pose] Stopped capturing");
        }

        private float _lastDiagTime;

        private void Update()
        {
            if (!isCapturing || arCamera == null || !_modeInitDone) return;

            // Periodic diagnostic (every 5 seconds)
            if (Time.time - _lastDiagTime > 5f)
            {
                _lastDiagTime = Time.time;
                Debug.LogError($"[AR Pose DIAG] mode={captureMode}, easyAR={_useEasyAR}, webCam={_useWebCam}, " +
                    $"frames={frameCount}, sending={_isSending}, arCapFail={_arCaptureFailCount}, " +
                    $"easyInit={easyARTracker?.IsInitialized}, easyTrack={easyARTracker?.IsTracking}");
            }

            // 1. Read VIO/orientation pose every frame
            Vector3 vioPos;
            Quaternion vioRot;

            if (_useEasyAR && easyARTracker != null)
            {
                // EasyAR mode: EasyAR controls camera transform directly (like ARCore)
                // Read pose from camera, fall back to gyro if not tracking yet
                if (easyARTracker.IsTracking)
                {
                    vioPos = arCamera.transform.position;
                    vioRot = arCamera.transform.rotation;
                }
                else
                {
                    vioPos = Vector3.zero;
                    vioRot = GetGyroRotation();
                }
            }
            else if (_useWebCam)
            {
                // WebCam mode: gyroscope for rotation, no position tracking
                vioPos = Vector3.zero;
                vioRot = GetGyroRotation();
            }
            else
            {
                // AR mode or Editor mode: read from camera transform
                vioPos = arCamera.transform.position;
                vioRot = arCamera.transform.rotation;
            }

            // 2. Apply client-side fusion (60fps smooth positioning)
            if (_fusion.HasOffset)
            {
                var (fusedPos, fusedRot) = _fusion.ApplyFusion(vioPos, vioRot, Time.deltaTime);
                ApplyWorldPose(fusedPos, fusedRot);
            }

            // 3. Check if we should trigger a VPS request
            if (!_isSending && _trigger.ShouldTriggerVps(Time.deltaTime, vioPos))
            {
                frameCount++;

                // Cache VIO pose at capture moment for offset calculation
                _captureVioPos = vioPos;
                _captureVioRot = vioRot;

                var vioPoseInput = new VioPoseInput
                {
                    position = new float[] { vioPos.x, vioPos.y, vioPos.z },
                    rotation = new float[] { vioRot.w, vioRot.x, vioRot.y, vioRot.z },
                    timestamp = Time.time
                };

                Texture2D image = CaptureFrame();
                if (image == null)
                {
                    _arCaptureFailCount++;
                    if (_arCaptureFailCount == AR_CAPTURE_FAIL_THRESHOLD && UseARCamera)
                    {
                        Debug.LogWarning($"[AR Pose] AR capture failed {AR_CAPTURE_FAIL_THRESHOLD} times");
                        DisableARComponents();
                        TryEasyAROrWebCam();
                    }
                    return;
                }

                _arCaptureFailCount = 0;
                Debug.Log($"[AR Pose] Frame {frameCount} captured: {image.width}x{image.height}, sending...");
                Intrinsics intrinsics = ComputeIntrinsics();

                if (vpsClient != null)
                    StartCoroutine(SendFrameAndWait(image, Time.time, intrinsics, vioPoseInput));
            }
        }

        /// <summary>
        /// Convert gyroscope attitude to Unity world rotation.
        /// </summary>
        private Quaternion GetGyroRotation()
        {
            if (!SystemInfo.supportsGyroscope || !Input.gyro.enabled)
                return arCamera.transform.rotation;

            // Gyro attitude → Unity coordinate conversion
            Quaternion gyro = Input.gyro.attitude;
            // Convert from right-hand to left-hand coordinate system
            Quaternion rot = new Quaternion(gyro.x, gyro.y, -gyro.z, -gyro.w);
            // Rotate to align with Unity's camera forward
            return Quaternion.Euler(90f, 0f, 0f) * rot;
        }

        private System.Collections.IEnumerator SendFrameAndWait(Texture2D image, float timestamp, Intrinsics intrinsics, VioPoseInput vioPose)
        {
            _isSending = true;
            yield return StartCoroutine(vpsClient.SendFrame(image, timestamp, intrinsics, vioPose));
            _isSending = false;
        }

        /// <summary>
        /// Called when VPS returns a fused pose. Updates client fusion offset.
        /// </summary>
        private void OnFusedPoseReceived(VpsPose fusedPose)
        {
            if (fusedPose == null || fusedPose.position == null || fusedPose.position.Length < 3) return;

            Vector3 vpsPos = new Vector3(fusedPose.position[0], fusedPose.position[1], fusedPose.position[2]);

            Quaternion vpsRot;
            if (fusedPose.rotation != null && fusedPose.rotation.Length >= 4)
            {
                // VPS: [qw, qx, qy, qz] -> Unity: Quaternion(qx, qy, qz, qw)
                vpsRot = new Quaternion(fusedPose.rotation[1], fusedPose.rotation[2],
                                        fusedPose.rotation[3], fusedPose.rotation[0]);
            }
            else
            {
                vpsRot = Quaternion.identity;
            }

            // Update client fusion offset
            _fusion.UpdateOffset(vpsPos, vpsRot, _captureVioPos, _captureVioRot);

            // Notify trigger controller
            _trigger.OnVpsResponse(
                vpsClient.TrackingState,
                true,
                fusedPose.confidence,
                _captureVioPos
            );

            Debug.Log($"[AR Pose] VPS offset updated. State={_trigger.TrackingState}, Drift={_trigger.EstimatedDrift:F2}m");
        }

        private void OnTrackingStateChanged(string newState)
        {
            // If server reports failure-related state change without a fused pose
            if (newState == "VPS_DEGRADED" || newState == "RELOCALIZING")
            {
                Vector3 pos = (_useWebCam || _useEasyAR) ? Vector3.zero : arCamera.transform.position;
                _trigger.OnVpsResponse(newState, false, 0f, pos);
            }
        }

        /// <summary>
        /// Apply world pose to camera/XR Origin.
        /// </summary>
        private void ApplyWorldPose(Vector3 worldPos, Quaternion worldRot)
        {
            if (UseARCamera && xrOrigin != null)
            {
                // Device with ARCore: adjust XR Origin so camera ends up at world pose
                Vector3 arLocalPos = arCamera.transform.localPosition;
                Quaternion arLocalRot = arCamera.transform.localRotation;

                Quaternion originRot = worldRot * Quaternion.Inverse(arLocalRot);
                Vector3 originPos = worldPos - originRot * arLocalPos;

                xrOrigin.transform.SetPositionAndRotation(originPos, originRot);
            }
            else
            {
                // EasyAR, WebCam or Editor: directly set camera
                arCamera.transform.SetPositionAndRotation(worldPos, worldRot);
            }
        }

        #region Frame Capture

        private Texture2D CaptureFrame()
        {
            if (UseARCamera)
                return CaptureFrameAR();
            else if (_useEasyAR && easyARTracker != null)
            {
                var img = easyARTracker.TryGetCameraImage();
                if (img == null && frameCount % 10 == 1)
                    Debug.LogError($"[AR Pose] EasyAR CaptureFrame returned null (init={easyARTracker.IsInitialized}, tracking={easyARTracker.IsTracking})");
                return img;
            }
            else if (_useWebCam)
                return CaptureFrameWebCam();
            else
                return CaptureFrameEditor();
        }

        private Texture2D CaptureFrameAR()
        {
            if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
                return null;

            int srcWidth = cpuImage.width;
            int srcHeight = cpuImage.height;
            int dstWidth = Mathf.Min(srcWidth, captureWidth) & ~1;
            int dstHeight = ((int)(dstWidth * ((float)srcHeight / srcWidth))) & ~1;

            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, srcWidth, srcHeight),
                outputDimensions = new Vector2Int(dstWidth, dstHeight),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.MirrorY
            };

            if (_captureTexture == null || _captureTexture.width != dstWidth || _captureTexture.height != dstHeight)
            {
                if (_captureTexture != null) Destroy(_captureTexture);
                _captureTexture = new Texture2D(dstWidth, dstHeight, TextureFormat.RGBA32, false);
            }

            var rawData = _captureTexture.GetRawTextureData<byte>();
            cpuImage.Convert(conversionParams, rawData);
            _captureTexture.Apply();
            cpuImage.Dispose();

            return _captureTexture;
        }

        private Texture2D CaptureFrameWebCam()
        {
            if (_webCam == null || !_webCam.isPlaying || !_webCam.didUpdateThisFrame)
                return null;

            int srcW = _webCam.width;
            int srcH = _webCam.height;

            // Skip if webcam not ready yet (returns 16x16 placeholder)
            if (srcW < 100) return null;

            int dstW = Mathf.Min(srcW, captureWidth);
            int dstH = (int)(dstW * ((float)srcH / srcW));

            // Get pixels from WebCamTexture
            if (_webCamScaled == null || _webCamScaled.width != dstW || _webCamScaled.height != dstH)
            {
                if (_webCamScaled != null) Destroy(_webCamScaled);
                _webCamScaled = new Texture2D(dstW, dstH, TextureFormat.RGB24, false);
            }

            // Use RenderTexture to scale
            RenderTexture rt = RenderTexture.GetTemporary(dstW, dstH, 0);
            Graphics.Blit(_webCam, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            _webCamScaled.ReadPixels(new Rect(0, 0, dstW, dstH), 0, 0);
            _webCamScaled.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            return _webCamScaled;
        }

        private Texture2D CaptureFrameEditor()
        {
            int width = arCamera.pixelWidth;
            int height = arCamera.pixelHeight;
            int dstWidth = Mathf.Min(width, captureWidth);
            int dstHeight = (int)(dstWidth * ((float)height / width));

            RenderTexture rt = RenderTexture.GetTemporary(dstWidth, dstHeight, 24);
            arCamera.targetTexture = rt;
            arCamera.Render();
            RenderTexture.active = rt;

            if (_captureTexture == null || _captureTexture.width != dstWidth || _captureTexture.height != dstHeight)
            {
                if (_captureTexture != null) Destroy(_captureTexture);
                _captureTexture = new Texture2D(dstWidth, dstHeight, TextureFormat.RGB24, false);
            }

            _captureTexture.ReadPixels(new Rect(0, 0, dstWidth, dstHeight), 0, 0);
            _captureTexture.Apply();

            arCamera.targetTexture = null;
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            return _captureTexture;
        }

        private Intrinsics ComputeIntrinsics()
        {
            Intrinsics result;
            string source;

            if (UseARCamera && arCameraManager.TryGetIntrinsics(out XRCameraIntrinsics arIntrinsics))
            {
                float scaleX = (float)captureWidth / arIntrinsics.resolution.x;
                result = new Intrinsics
                {
                    fx = arIntrinsics.focalLength.x * scaleX,
                    fy = arIntrinsics.focalLength.y * scaleX,
                    cx = arIntrinsics.principalPoint.x * scaleX,
                    cy = arIntrinsics.principalPoint.y * scaleX
                };
                source = "ARCore";
            }
            else if (_useEasyAR && easyARTracker != null &&
                easyARTracker.TryGetIntrinsics(out float eFx, out float eFy, out float eCx, out float eCy))
            {
                result = new Intrinsics { fx = eFx, fy = eFy, cx = eCx, cy = eCy };
                source = "EasyAR";
            }
            else if (_useWebCam && _webCam != null && _webCam.width > 100)
            {
                // Estimate intrinsics for phone camera (~60° FOV)
                int w = Mathf.Min(_webCam.width, captureWidth);
                int h = (int)(w * ((float)_webCam.height / _webCam.width));
                float f = w * 0.8f; // ~60° horizontal FOV
                result = new Intrinsics
                {
                    fx = f,
                    fy = f,
                    cx = w * 0.5f,
                    cy = h * 0.5f
                };
                source = "WebCam (est)";
            }
            else
            {
                // Editor fallback
                float camW = arCamera.pixelWidth;
                float camH = arCamera.pixelHeight;
                float vFov = arCamera.fieldOfView * Mathf.Deg2Rad;
                float fy2 = (camH * 0.5f) / Mathf.Tan(vFov * 0.5f);
                float capW = Mathf.Min(camW, captureWidth);
                float scale = capW / camW;
                result = new Intrinsics
                {
                    fx = fy2 * scale,
                    fy = fy2 * scale,
                    cx = camW * 0.5f * scale,
                    cy = camH * 0.5f * scale
                };
                source = "Editor (est)";
            }

            _lastIntrinsics = result;
            _intrinsicsSource = source;
            Debug.Log($"[VPS Intrinsics] source={source} fx={result.fx:F1} fy={result.fy:F1} cx={result.cx:F1} cy={result.cy:F1}");
            return result;
        }

        #endregion

        private void OnDestroy()
        {
            if (vpsClient != null)
            {
                vpsClient.OnFusedPoseReceived -= OnFusedPoseReceived;
                vpsClient.OnTrackingStateChanged -= OnTrackingStateChanged;
            }

            if (_webCam != null)
            {
                _webCam.Stop();
                Destroy(_webCam);
            }

            if (_captureTexture != null)
                Destroy(_captureTexture);

            if (_webCamScaled != null)
                Destroy(_webCamScaled);
        }

        private void OnApplicationQuit()
        {
            StopCapturing();
        }
    }
}
