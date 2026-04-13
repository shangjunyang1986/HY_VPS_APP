using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace VPS
{
    /// <summary>
    /// Manages the complete scan flow: frame capture → ZIP → upload → build map.
    /// Works with EasyARTracker for camera frames and intrinsics.
    /// </summary>
    public class VpsScanManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private VpsScanClient scanClient;
        [SerializeField] private EasyARTracker easyARTracker;

        [Header("Scan Settings")]
        [SerializeField] private string deviceModel = "vivo_s6";
        [SerializeField] private float captureIntervalSec = 0.5f;
        [SerializeField] private int maxFrames = 200;
        [SerializeField] private int jpegQuality = 85;

        [Header("Status (Read-Only)")]
        [SerializeField] private ScanState state = ScanState.Idle;
        [SerializeField] private int capturedFrames;
        [SerializeField] private string currentCaptureId;
        [SerializeField] private string currentMapId;
        [SerializeField] private string statusMessage = "";

        // Scan state machine
        public enum ScanState
        {
            Idle,
            Scanning,
            Packaging,
            Uploading,
            Building,
            Done,
            Error
        }

        // Intrinsics captured during scan
        private float _fx, _fy, _cx, _cy;
        private int _imgWidth, _imgHeight;
        private bool _hasIntrinsics;

        // Frame saving
        private string _framesDir;
        private string _zipPath;
        private string _vioTrajectoryPath;
        private float _lastCaptureTime;

        // VIO pose recording: frame_name -> (position, rotation)
        private readonly List<string> _poseFrameNames = new List<string>();
        private readonly List<Vector3> _posePositions = new List<Vector3>();
        private readonly List<Quaternion> _poseRotations = new List<Quaternion>();

        // Public accessors
        public ScanState State => state;
        public int CapturedFrames => capturedFrames;
        public string CurrentCaptureId => currentCaptureId;
        public string CurrentMapId => currentMapId;
        public string StatusMessage => statusMessage;

        // Events
        public event Action<ScanState> OnStateChanged;
        public event Action<string> OnScanComplete;
        public event Action<string> OnError;

        private void Start()
        {
            if (scanClient == null) scanClient = FindAnyObjectByType<VpsScanClient>();
            if (easyARTracker == null) easyARTracker = FindAnyObjectByType<EasyARTracker>();

            if (scanClient != null)
            {
                scanClient.OnUploadComplete += OnUploadDone;
                scanClient.OnUploadFailed += OnUploadError;
                scanClient.OnMapStatusUpdated += OnMapStatus;
            }
        }

        /// <summary>
        /// Start scanning — captures camera frames to JPEG files.
        /// </summary>
        public void StartScan()
        {
            if (state != ScanState.Idle && state != ScanState.Done && state != ScanState.Error)
            {
                Debug.LogWarning("[ScanMgr] Cannot start scan in state: " + state);
                return;
            }

            // Create temp directory for frames
            _framesDir = Path.Combine(Application.temporaryCachePath, "scan_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(_framesDir);

            capturedFrames = 0;
            _hasIntrinsics = false;
            _lastCaptureTime = 0f;
            _vioTrajectoryPath = null;
            currentCaptureId = null;
            currentMapId = null;

            _poseFrameNames.Clear();
            _posePositions.Clear();
            _poseRotations.Clear();

            SetState(ScanState.Scanning);
            statusMessage = "Scanning...";
            Debug.Log("[ScanMgr] Scan started, saving frames to: " + _framesDir);
        }

        /// <summary>
        /// Stop scanning and begin the upload pipeline.
        /// </summary>
        public void StopScanAndUpload()
        {
            if (state != ScanState.Scanning)
            {
                Debug.LogWarning("[ScanMgr] Cannot stop scan in state: " + state);
                return;
            }

            if (capturedFrames < 5)
            {
                statusMessage = "Too few frames (" + capturedFrames + "), need at least 5";
                SetState(ScanState.Error);
                return;
            }

            Debug.Log("[ScanMgr] Scan stopped with " + capturedFrames + " frames");
            StartCoroutine(PackageAndUpload());
        }

        /// <summary>
        /// Cancel current scan without uploading.
        /// </summary>
        public void CancelScan()
        {
            StopAllCoroutines();
            CleanupTempFiles();
            SetState(ScanState.Idle);
            statusMessage = "Cancelled";
        }

        private void Update()
        {
            if (state != ScanState.Scanning) return;
            if (easyARTracker == null || !easyARTracker.IsInitialized) return;

            // Capture frames at interval
            if (Time.time - _lastCaptureTime < captureIntervalSec) return;
            if (capturedFrames >= maxFrames)
            {
                Debug.Log("[ScanMgr] Max frames reached, stopping scan");
                StopScanAndUpload();
                return;
            }

            CaptureOneFrame();
        }

        private void CaptureOneFrame()
        {
            Texture2D image = easyARTracker.TryGetCameraImage();
            if (image == null) return;

            // Save intrinsics from first valid frame
            if (!_hasIntrinsics)
            {
                if (easyARTracker.TryGetIntrinsics(out float fx, out float fy, out float cx, out float cy))
                {
                    _fx = fx;
                    _fy = fy;
                    _cx = cx;
                    _cy = cy;
                    _imgWidth = image.width;
                    _imgHeight = image.height;
                    _hasIntrinsics = true;
                    Debug.Log(string.Format("[ScanMgr] Intrinsics: fx={0:F1} fy={1:F1} cx={2:F1} cy={3:F1} size={4}x{5}",
                        fx, fy, cx, cy, _imgWidth, _imgHeight));
                }
            }

            // Encode and save JPEG
            byte[] jpgData = image.EncodeToJPG(jpegQuality);
            string filename = string.Format("frame_{0:D5}.jpg", capturedFrames);
            string filepath = Path.Combine(_framesDir, filename);
            try
            {
                File.WriteAllBytes(filepath, jpgData);
            }
            catch (Exception ex)
            {
                Debug.LogError("[ScanMgr] Failed to save frame: " + ex.Message);
                statusMessage = "Storage error: " + ex.Message;
                SetState(ScanState.Error);
                OnError?.Invoke(statusMessage);
                return;
            }

            // Record VIO pose for this frame
            if (easyARTracker.TryGetPose(out Vector3 pos, out Quaternion rot))
            {
                _poseFrameNames.Add(filename);
                _posePositions.Add(pos);
                _poseRotations.Add(rot);
            }

            capturedFrames++;
            _lastCaptureTime = Time.time;

            if (capturedFrames % 20 == 0)
                Debug.Log("[ScanMgr] Captured " + capturedFrames + " frames, " + _poseFrameNames.Count + " poses");
        }

        private IEnumerator PackageAndUpload()
        {
            // Step 1: Package frames into ZIP
            SetState(ScanState.Packaging);
            statusMessage = "Packaging " + capturedFrames + " frames...";

            _zipPath = Path.Combine(Application.temporaryCachePath, "scan_frames.zip");
            if (File.Exists(_zipPath)) File.Delete(_zipPath);

            // ZIP creation (runs synchronously but is fast for JPEG files)
            yield return null; // Give UI a frame to update

            try
            {
                ZipFile.CreateFromDirectory(_framesDir, _zipPath);
                long zipSizeMB = new FileInfo(_zipPath).Length / (1024 * 1024);
                Debug.Log("[ScanMgr] ZIP created: " + _zipPath + " (" + zipSizeMB + " MB)");
            }
            catch (Exception ex)
            {
                statusMessage = "ZIP failed: " + ex.Message;
                SetState(ScanState.Error);
                OnError?.Invoke(statusMessage);
                yield break;
            }

            // Step 1b: Write VIO trajectory JSON
            if (_poseFrameNames.Count >= 3)
            {
                _vioTrajectoryPath = Path.Combine(Application.temporaryCachePath, "vio_trajectory.json");
                try
                {
                    WriteVioTrajectory(_vioTrajectoryPath);
                    Debug.Log("[ScanMgr] VIO trajectory saved: " + _poseFrameNames.Count + " poses");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[ScanMgr] Failed to write VIO trajectory: " + ex.Message);
                    _vioTrajectoryPath = null;
                }
            }
            else
            {
                Debug.LogWarning("[ScanMgr] Too few VIO poses (" + _poseFrameNames.Count + "), skipping trajectory export");
            }

            // Step 2: Create capture on server
            SetState(ScanState.Uploading);
            statusMessage = "Creating capture...";

            string captureId = null;
            yield return scanClient.CreateCapture(deviceModel, id => captureId = id);

            if (string.IsNullOrEmpty(captureId))
            {
                statusMessage = "Failed to create capture";
                SetState(ScanState.Error);
                OnError?.Invoke(statusMessage);
                yield break;
            }

            currentCaptureId = captureId;
            statusMessage = "Uploading frames...";

            // Step 3: Upload ZIP + intrinsics
            if (!_hasIntrinsics)
            {
                // Fallback intrinsics
                _fx = _imgWidth * 0.8f;
                _fy = _fx;
                _cx = _imgWidth * 0.5f;
                _cy = _imgHeight * 0.5f;
                Debug.LogWarning("[ScanMgr] Using estimated intrinsics (no EasyAR intrinsics available)");
            }

            yield return scanClient.UploadCapture(
                captureId, _zipPath,
                _fx, _fy, _cx, _cy,
                _imgWidth, _imgHeight, 1,
                deviceModel,
                _vioTrajectoryPath);

            // Upload result handled by event callbacks
        }

        private void OnUploadDone(string captureId)
        {
            if (captureId != currentCaptureId) return;

            statusMessage = "Upload done, building map...";
            StartCoroutine(TriggerMapBuild(captureId));
        }

        private void OnUploadError(string captureId, string error)
        {
            if (captureId != currentCaptureId) return;

            statusMessage = "Upload failed: " + error;
            SetState(ScanState.Error);
            OnError?.Invoke(error);
        }

        private IEnumerator TriggerMapBuild(string captureId)
        {
            SetState(ScanState.Building);

            string mapId = null;
            yield return scanClient.CreateMap(captureId, true, id => mapId = id);

            if (string.IsNullOrEmpty(mapId))
            {
                statusMessage = "Failed to create map";
                SetState(ScanState.Error);
                yield break;
            }

            currentMapId = mapId;
            statusMessage = "Building map: " + mapId;

            // Poll until done
            yield return scanClient.PollMapStatus(mapId, 3f);

            // Final status check
            MapStatusResp finalStatus = null;
            yield return scanClient.QueryMapStatus(mapId, s => finalStatus = s);

            if (finalStatus != null && finalStatus.status == "ready")
            {
                statusMessage = "Map ready: " + mapId;
                SetState(ScanState.Done);
                OnScanComplete?.Invoke(mapId);
                CleanupTempFiles();
            }
            else
            {
                string buildStatus = finalStatus != null ? finalStatus.build_status : "unknown";
                statusMessage = "Map build " + buildStatus;
                if (buildStatus == "failed")
                    SetState(ScanState.Error);
                else
                    SetState(ScanState.Done);
            }
        }

        private void OnMapStatus(MapStatusResp resp)
        {
            if (resp.map_id != currentMapId) return;
            statusMessage = "Building... (" + resp.build_status + ")";
        }

        private void SetState(ScanState newState)
        {
            if (state == newState) return;
            state = newState;
            Debug.Log("[ScanMgr] State -> " + newState);
            OnStateChanged?.Invoke(newState);
        }

        private void CleanupTempFiles()
        {
            try
            {
                if (!string.IsNullOrEmpty(_framesDir) && Directory.Exists(_framesDir))
                    Directory.Delete(_framesDir, true);
                if (!string.IsNullOrEmpty(_zipPath) && File.Exists(_zipPath))
                    File.Delete(_zipPath);
                if (!string.IsNullOrEmpty(_vioTrajectoryPath) && File.Exists(_vioTrajectoryPath))
                    File.Delete(_vioTrajectoryPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ScanMgr] Cleanup failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Write VIO trajectory to JSON file in the format expected by align_scale.py.
        /// Format: { "poses": { "frame_00000.jpg": { "position": [x,y,z], "rotation": [qw,qx,qy,qz] } } }
        /// </summary>
        private void WriteVioTrajectory(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"poses\": {");

            for (int i = 0; i < _poseFrameNames.Count; i++)
            {
                Vector3 p = _posePositions[i];
                Quaternion r = _poseRotations[i];

                sb.Append("    \"");
                sb.Append(_poseFrameNames[i]);
                sb.Append("\": { \"position\": [");
                sb.Append(p.x.ToString("G9", CultureInfo.InvariantCulture)); sb.Append(", ");
                sb.Append(p.y.ToString("G9", CultureInfo.InvariantCulture)); sb.Append(", ");
                sb.Append(p.z.ToString("G9", CultureInfo.InvariantCulture));
                sb.Append("], \"rotation\": [");
                sb.Append(r.w.ToString("G9", CultureInfo.InvariantCulture)); sb.Append(", ");
                sb.Append(r.x.ToString("G9", CultureInfo.InvariantCulture)); sb.Append(", ");
                sb.Append(r.y.ToString("G9", CultureInfo.InvariantCulture)); sb.Append(", ");
                sb.Append(r.z.ToString("G9", CultureInfo.InvariantCulture));
                sb.Append("] }");

                if (i < _poseFrameNames.Count - 1) sb.Append(",");
                sb.AppendLine();
            }

            sb.AppendLine("  }");
            sb.AppendLine("}");

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private void OnDestroy()
        {
            if (scanClient != null)
            {
                scanClient.OnUploadComplete -= OnUploadDone;
                scanClient.OnUploadFailed -= OnUploadError;
                scanClient.OnMapStatusUpdated -= OnMapStatus;
            }

            CleanupTempFiles();
        }
    }
}
