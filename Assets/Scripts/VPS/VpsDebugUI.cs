using System;
using System.Collections;
using System.Collections.Generic;
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
        [SerializeField] private VpsScanUI scanUI;
        [SerializeField] private VpsScanClient scanClient;

        [Header("Display Settings")]
        [SerializeField] private bool showDebugUI = true;
        [SerializeField] private int fontSize = 16;
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
        private GUIStyle _buttonStyle;
        private GUIStyle _buttonActiveStyle;
        private float _guiScale = 1f;

        // Map picker state
        private bool _showMapPicker;
        private bool _isFetchingMaps;
        private string _mapFetchError;
        private MapStatusResp[] _mapList;
        private Vector2 _mapScrollPos;

        private void Start()
        {
            if (vpsClient == null) vpsClient = FindAnyObjectByType<VpsClient>();
            if (arPoseHandler == null) arPoseHandler = FindAnyObjectByType<ArPoseHandler>();
            if (scanUI == null) scanUI = FindAnyObjectByType<VpsScanUI>();
            if (scanClient == null) scanClient = FindAnyObjectByType<VpsScanClient>();

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

            // Hide debug HUD when in scan mode to avoid UI overlap
            if (scanUI != null && scanUI.IsScanMode) return;

            InitStyles();

            // Apply DPI scaling via GUI.matrix
            Matrix4x4 savedMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                new Vector3(_guiScale, _guiScale, 1f));

            // All coordinates below are in logical pixels (pre-scale).
            // Cap panel size so nothing overflows on high-DPI portrait phones.
            float scaledScreenW = Screen.width / _guiScale;
            float scaledScreenH = Screen.height / _guiScale;
            float panelWidth = Mathf.Min(420f, scaledScreenW - 16f);
            float panelHeight = Mathf.Min(580f, scaledScreenH - 16f);

            // Position at top-left with margin
            Rect panelRect = new Rect(8, 8, panelWidth, panelHeight);
            GUI.Box(panelRect, "", _boxStyle);
            GUILayout.BeginArea(new Rect(16, 12, panelWidth - 16, panelHeight - 12));

            // Header row with Maps button
            GUILayout.BeginHorizontal();
            GUILayout.Label("VPS Debug", _headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Maps", _buttonStyle, GUILayout.Width(80), GUILayout.Height(36)))
            {
                _showMapPicker = !_showMapPicker;
                if (_showMapPicker && !_isFetchingMaps)
                    StartCoroutine(FetchMapsCoroutine());
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            // Current map
            string curMap = vpsClient != null ? vpsClient.MapId : "N/A";
            DrawLabel($"Map: <color=#88CCFF>{TruncateId(curMap)}</color>");
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

                // Quality, inliers, and fusion participation
                string ql = vpsClient != null ? vpsClient.LastQualityLevel : "";
                int inliers = vpsClient != null ? vpsClient.LastNumInliers : 0;
                bool usedFusion = vpsClient != null && vpsClient.LastVpsUsedForFusion;
                string qlColor = ql == "high" ? "#00FF88" : ql == "medium" ? "#FFAA00" : "#FF4444";
                string fusionMark = usedFusion ? "<color=#00FF88>Y</color>" : "<color=#FF4444>N</color>";
                DrawLabel($"Q: <color={qlColor}>{ql}</color>  Inliers: {inliers}  Fused: {fusionMark}");
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

            // Map picker overlay (drawn last so it sits on top of the debug panel)
            if (_showMapPicker)
                DrawMapPicker(scaledScreenW, scaledScreenH);

            // Restore matrix
            GUI.matrix = savedMatrix;
        }

        private void DrawMapPicker(float sw, float sh)
        {
            float pw = Mathf.Min(480f, sw - 32f);
            float ph = Mathf.Min(520f, sh - 120f);
            float px = (sw - pw) / 2f;
            float py = (sh - ph) / 2f;

            GUI.Box(new Rect(px, py, pw, ph), "", _boxStyle);
            GUILayout.BeginArea(new Rect(px + 12, py + 12, pw - 24, ph - 24));

            // Header row
            GUILayout.BeginHorizontal();
            GUILayout.Label("Select Map", _headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("X", _buttonStyle, GUILayout.Width(40), GUILayout.Height(32)))
                _showMapPicker = false;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Refresh button
            GUI.enabled = !_isFetchingMaps;
            if (GUILayout.Button(_isFetchingMaps ? "Loading..." : "Refresh", _buttonStyle, GUILayout.Height(32)))
                StartCoroutine(FetchMapsCoroutine());
            GUI.enabled = true;

            GUILayout.Space(4);

            if (_isFetchingMaps && (_mapList == null || _mapList.Length == 0))
            {
                GUILayout.Label("<color=#FFAA00>Loading maps...</color>", _labelStyle);
            }
            else if (!string.IsNullOrEmpty(_mapFetchError))
            {
                GUILayout.Label("<color=#FF4444>" + _mapFetchError + "</color>", _labelStyle);
            }
            else if (_mapList == null || _mapList.Length == 0)
            {
                GUILayout.Label("No maps available", _labelStyle);
            }
            else
            {
                // Filter: only ready-for-localization maps, newest first
                var filtered = new List<MapStatusResp>();
                foreach (var m in _mapList)
                {
                    if (m != null && m.ready_for_localization && m.status == "ready")
                        filtered.Add(m);
                }
                filtered.Sort((a, b) => string.Compare(b.map_id ?? "", a.map_id ?? "", StringComparison.Ordinal));

                string currentMapId = vpsClient != null ? vpsClient.MapId : "";

                GUILayout.Label($"<color=#AAAAAA>{filtered.Count} maps (tap to switch)</color>", _labelStyle);
                GUILayout.Space(2);

                _mapScrollPos = GUILayout.BeginScrollView(_mapScrollPos);
                foreach (var m in filtered)
                {
                    bool isCurrent = m.map_id == currentMapId;
                    string mark = m.metric_aligned ? " [calib]" : "";
                    string label = (isCurrent ? "* " : "  ") + m.map_id + mark;
                    var style = isCurrent ? _buttonActiveStyle : _buttonStyle;
                    if (GUILayout.Button(label, style, GUILayout.Height(44)))
                    {
                        if (!isCurrent)
                        {
                            SwitchToMap(m.map_id);
                            _showMapPicker = false;
                        }
                    }
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndArea();
        }

        private IEnumerator FetchMapsCoroutine()
        {
            if (scanClient == null)
            {
                _mapFetchError = "ScanClient not found";
                yield break;
            }

            _isFetchingMaps = true;
            _mapFetchError = null;

            MapStatusResp[] result = null;
            yield return scanClient.ListMaps(list => result = list);

            _mapList = result;
            _isFetchingMaps = false;
            if (result == null)
                _mapFetchError = "Failed to load maps";
        }

        private void SwitchToMap(string newMapId)
        {
            if (vpsClient == null || string.IsNullOrEmpty(newMapId)) return;
            if (vpsClient.MapId == newMapId) return;

            Debug.Log("[DebugUI] Switching map: " + vpsClient.MapId + " -> " + newMapId);

            bool wasCapturing = arPoseHandler != null && arPoseHandler.IsCapturing;
            if (wasCapturing)
                arPoseHandler.StopCapturing();

            vpsClient.MapId = newMapId;

            if (wasCapturing && arPoseHandler != null)
                arPoseHandler.StartCapturing();
        }

        private string TruncateId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "N/A";
            if (id.Length <= 26) return id;
            return id.Substring(0, 14) + "..." + id.Substring(id.Length - 8);
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

            _buttonStyle = new GUIStyle(GUI.skin.button);
            _buttonStyle.fontSize = fontSize;
            _buttonStyle.normal.textColor = Color.white;
            _buttonStyle.richText = true;

            _buttonActiveStyle = new GUIStyle(GUI.skin.button);
            _buttonActiveStyle.fontSize = fontSize;
            _buttonActiveStyle.fontStyle = FontStyle.Bold;
            _buttonActiveStyle.normal.textColor = new Color(0.3f, 1f, 0.5f);
            _buttonActiveStyle.normal.background = MakeTexture(2, 2, new Color(0.1f, 0.3f, 0.15f, 0.9f));
            _buttonActiveStyle.richText = true;
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
