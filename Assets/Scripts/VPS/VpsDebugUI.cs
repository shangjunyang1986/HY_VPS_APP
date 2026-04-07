using UnityEngine;

namespace VPS
{
    /// <summary>
    /// Debug HUD for VPS - DPI-adaptive display with intrinsics diagnostics.
    /// Uses GUI.matrix scaling so all layout is in logical pixels (160 DPI baseline).
    /// </summary>
    public class VpsDebugUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private VpsClient vpsClient;
        [SerializeField] private ArPoseHandler arPoseHandler;

        [Header("Display Settings")]
        [SerializeField] private bool showDebugUI = true;
        [SerializeField] private int fontSize = 22;
        [SerializeField] private float baseDpi = 160f;

        private float _fps;
        private float _fpsTimer;
        private int _fpsFrameCount;
        private VpsPose _lastPose;
        private VpsPose _lastFusedPose;

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _sectionStyle;
        private float _guiScale = 1f;

        private void Start()
        {
            if (vpsClient == null) vpsClient = FindAnyObjectByType<VpsClient>();
            if (arPoseHandler == null) arPoseHandler = FindAnyObjectByType<ArPoseHandler>();

            if (vpsClient != null)
            {
                vpsClient.OnPoseReceived += pose => _lastPose = pose;
                vpsClient.OnFusedPoseReceived += pose => _lastFusedPose = pose;
            }

            ComputeGuiScale();
        }

        private void ComputeGuiScale()
        {
            float dpi = Screen.dpi;
            if (dpi <= 0) dpi = 160f;
            _guiScale = dpi / baseDpi;
            // Clamp to reasonable range
            _guiScale = Mathf.Clamp(_guiScale, 1f, 4f);
        }

        private void Update()
        {
            _fpsFrameCount++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f)
            {
                _fps = _fpsFrameCount / _fpsTimer;
                _fpsFrameCount = 0;
                _fpsTimer = 0f;
            }
        }

        private void OnGUI()
        {
            if (!showDebugUI) return;

            InitStyles();

            // Apply DPI scaling via GUI.matrix
            Matrix4x4 savedMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                new Vector3(_guiScale, _guiScale, 1f));

            // All coordinates below are in logical pixels (pre-scale)
            float panelWidth = 420;
            float panelHeight = 560;
            float scaledScreenW = Screen.width / _guiScale;
            float scaledScreenH = Screen.height / _guiScale;

            // Position at top-left with margin
            Rect panelRect = new Rect(8, 8, panelWidth, panelHeight);
            GUI.Box(panelRect, "", _boxStyle);
            GUILayout.BeginArea(new Rect(16, 12, panelWidth - 16, panelHeight - 12));

            GUILayout.Label("VPS Debug", _headerStyle);
            GUILayout.Space(4);

            // ---- Connection & Mode ----
            bool connected = vpsClient != null && vpsClient.IsConnected;
            string connColor = connected ? "#00FF88" : "#FF4444";
            DrawLabel($"Conn: <color={connColor}>{(connected ? "OK" : "OFF")}</color>    Mode: <color=#88CCFF>{(arPoseHandler != null ? arPoseHandler.CaptureMode : "N/A")}</color>");

            // Session ID
            string sid = vpsClient != null ? vpsClient.SessionId : "N/A";
            if (!string.IsNullOrEmpty(sid) && sid.Length > 12)
                sid = sid.Substring(0, 12) + "...";
            DrawLabel($"Session: {sid}");

            // Tracking state
            string state = vpsClient != null ? vpsClient.TrackingState : "N/A";
            string stateColor = GetStateColor(state);
            DrawLabel($"Tracking: <color={stateColor}>{state}</color>");

            GUILayout.Space(4);

            // ---- Trigger & Drift ----
            var trigger = arPoseHandler?.Trigger;
            if (trigger != null)
            {
                float interval = trigger.CurrentInterval;
                bool isFast = interval < 1.5f;
                string freqColor = isFast ? "#FFAA00" : "#00FF88";
                float drift = trigger.EstimatedDrift;
                string driftColor = drift < 0.3f ? "#00FF88" : drift < 0.5f ? "#FFAA00" : "#FF4444";
                DrawLabel($"Interval: <color={freqColor}>{interval:F1}s</color>    Drift: <color={driftColor}>{drift:F2}m</color>");

                if (trigger.ConsecutiveFailures > 0)
                    DrawLabel($"<color=#FF4444>Failures: {trigger.ConsecutiveFailures}</color>");
            }

            // Fusion
            var fusion = arPoseHandler?.Fusion;
            if (fusion != null)
            {
                string fusionStatus = fusion.HasOffset ? "Active" : "Waiting";
                string fusionColor = fusion.HasOffset ? "#00FF88" : "#FFAA00";
                DrawLabel($"Fusion: <color={fusionColor}>{fusionStatus}</color>");
            }

            GUILayout.Space(4);

            // ---- Pose Data ----
            DrawSection("Position");
            if (_lastPose != null && _lastPose.position != null)
            {
                DrawLabel($"VPS: ({_lastPose.position[0]:F2}, {_lastPose.position[1]:F2}, {_lastPose.position[2]:F2})");
                DrawLabel($"Confidence: {_lastPose.confidence:F3}");
            }
            else
            {
                DrawLabel("VPS: waiting...");
            }

            if (_lastFusedPose != null && _lastFusedPose.position != null)
            {
                DrawLabel($"Fused: ({_lastFusedPose.position[0]:F2}, {_lastFusedPose.position[1]:F2}, {_lastFusedPose.position[2]:F2})");
            }

            GUILayout.Space(4);

            // ---- Intrinsics Diagnostics ----
            DrawSection("Intrinsics");
            string src = arPoseHandler != null ? arPoseHandler.IntrinsicsSource : "N/A";
            string srcColor = (src == "EasyAR" || src == "ARCore") ? "#00FF88" : "#FF8800";
            DrawLabel($"Source: <color={srcColor}>{src}</color>");

            var intr = arPoseHandler?.LastIntrinsics;
            if (intr != null)
            {
                DrawLabel($"fx={intr.fx:F1}  fy={intr.fy:F1}");
                DrawLabel($"cx={intr.cx:F1}  cy={intr.cy:F1}");
            }
            else
            {
                DrawLabel("No intrinsics yet");
            }

            GUILayout.Space(4);

            // ---- Performance ----
            DrawSection("Performance");
            float latency = vpsClient != null ? vpsClient.LastLatencyMs : 0f;
            string latColor = latency < 100 ? "#00FF88" : latency < 300 ? "#FFAA00" : "#FF4444";
            string fpsColor = _fps >= 30 ? "#00FF88" : _fps >= 15 ? "#FFAA00" : "#FF4444";
            DrawLabel($"Latency: <color={latColor}>{latency:F0}ms</color>    FPS: <color={fpsColor}>{_fps:F0}</color>");

            int frames = arPoseHandler != null ? arPoseHandler.FrameCount : 0;
            DrawLabel($"VPS Frames: {frames}");

            GUILayout.EndArea();

            // Restore matrix
            GUI.matrix = savedMatrix;
        }

        private void InitStyles()
        {
            if (_boxStyle != null) return;

            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = MakeTexture(2, 2, new Color(0f, 0f, 0f, 0.75f));

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = fontSize;
            _labelStyle.normal.textColor = Color.white;
            _labelStyle.richText = true;
            _labelStyle.wordWrap = false;

            _headerStyle = new GUIStyle(GUI.skin.label);
            _headerStyle.fontSize = fontSize + 4;
            _headerStyle.fontStyle = FontStyle.Bold;
            _headerStyle.normal.textColor = new Color(0.3f, 0.85f, 1f);

            _sectionStyle = new GUIStyle(GUI.skin.label);
            _sectionStyle.fontSize = fontSize - 2;
            _sectionStyle.fontStyle = FontStyle.Bold;
            _sectionStyle.normal.textColor = new Color(0.7f, 0.7f, 0.7f);
        }

        private string GetStateColor(string state)
        {
            return state switch
            {
                "VPS_LOCKED" => "#00FF88",
                "INIT" => "#FFAA00",
                "VPS_DEGRADED" => "#FF8800",
                "RELOCALIZING" => "#FF4444",
                _ => "#FFFFFF"
            };
        }

        private void DrawLabel(string text)
        {
            GUILayout.Label(text, _labelStyle);
        }

        private void DrawSection(string title)
        {
            GUILayout.Label($"-- {title} --", _sectionStyle);
        }

        private Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;

            Texture2D tex = new Texture2D(width, height);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
