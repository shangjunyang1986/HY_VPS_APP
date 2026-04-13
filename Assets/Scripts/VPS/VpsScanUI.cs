using UnityEngine;

namespace VPS
{
    /// <summary>
    /// DPI-adaptive OnGUI overlay for scan mode.
    /// Shows scan/upload/build status and control buttons.
    /// Positioned at bottom-right to avoid overlapping VpsDebugUI (top-left).
    /// </summary>
    public class VpsScanUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private VpsScanManager scanManager;
        [SerializeField] private VpsClient vpsClient;
        [SerializeField] private ArPoseHandler arPoseHandler;

        [Header("Display Settings")]
        [SerializeField] private bool showUI = true;
        [SerializeField] private int fontSize = 16;
        [SerializeField] private float baseDpi = 160f;

        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _headerStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _buttonActiveStyle;
        private float _guiScale = 1f;
        private bool _stylesInit;

        // Mode toggle: false = localization, true = scan
        private bool _scanMode;

        /// <summary>Whether the UI is currently in scan mode (used by VpsDebugUI to hide itself).</summary>
        public bool IsScanMode => _scanMode;

        private void Start()
        {
            if (scanManager == null) scanManager = FindAnyObjectByType<VpsScanManager>();
            if (vpsClient == null) vpsClient = FindAnyObjectByType<VpsClient>();
            if (arPoseHandler == null) arPoseHandler = FindAnyObjectByType<ArPoseHandler>();

            float dpi = Screen.dpi;
            if (dpi <= 0) dpi = 160f;
            _guiScale = Mathf.Clamp(dpi / baseDpi, 1f, 4f);
        }

        private void OnGUI()
        {
            if (!showUI) return;

            InitStyles();

            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                new Vector3(_guiScale, _guiScale, 1f));

            float sw = Screen.width / _guiScale;
            float sh = Screen.height / _guiScale;

            // Mode toggle button (top-right)
            DrawModeToggle(sw);

            if (_scanMode)
                DrawScanPanel(sw, sh);

            GUI.matrix = saved;
        }

        private void DrawModeToggle(float sw)
        {
            float bw = 160f;
            float bh = 50f;
            float bx = sw - bw - 8f;
            float by = 58f;

            string label = _scanMode ? "Localize" : "Scan";
            GUIStyle style = _scanMode ? _buttonActiveStyle : _buttonStyle;

            if (GUI.Button(new Rect(bx, by, bw, bh), label, style))
            {
                _scanMode = !_scanMode;

                if (_scanMode)
                {
                    // Enter scan mode: pause and stop localization
                    if (arPoseHandler != null)
                    {
                        arPoseHandler.PausedForScan = true;
                        if (arPoseHandler.IsCapturing)
                            arPoseHandler.StopCapturing();
                    }
                }
                else
                {
                    // Exit scan mode: cancel scan if active, resume localization
                    if (scanManager != null &&
                        scanManager.State == VpsScanManager.ScanState.Scanning)
                        scanManager.CancelScan();

                    if (arPoseHandler != null)
                    {
                        arPoseHandler.PausedForScan = false;
                        if (!arPoseHandler.IsCapturing)
                            arPoseHandler.StartCapturing();
                    }
                }
            }
        }

        private void DrawScanPanel(float sw, float sh)
        {
            // Cap pw so panel never extends past the left edge on high-DPI phones.
            float pw = Mathf.Min(360f, sw - 16f);
            float ph = 440f;
            float px = sw - pw - 8f;
            float py = 70f;

            GUI.Box(new Rect(px, py, pw, ph), "", _boxStyle);
            GUILayout.BeginArea(new Rect(px + 12, py + 8, pw - 24, ph - 16));

            GUILayout.Label("Scan Mode", _headerStyle);
            GUILayout.Space(4);

            if (scanManager == null)
            {
                GUILayout.Label("ScanManager not found", _labelStyle);
                GUILayout.EndArea();
                return;
            }

            var state = scanManager.State;

            // Status info
            string stateColor = GetStateColor(state);
            GUILayout.Label(string.Format("State: <color={0}>{1}</color>", stateColor, state), _labelStyle);
            GUILayout.Label("Frames: " + scanManager.CapturedFrames, _labelStyle);

            if (!string.IsNullOrEmpty(scanManager.StatusMessage))
                GUILayout.Label(scanManager.StatusMessage, _labelStyle);

            if (!string.IsNullOrEmpty(scanManager.CurrentMapId))
                GUILayout.Label("Map: " + scanManager.CurrentMapId, _labelStyle);

            GUILayout.Space(8);

            // Action buttons
            float btnW = pw - 40f;
            float btnH = 48f;

            switch (state)
            {
                case VpsScanManager.ScanState.Idle:
                case VpsScanManager.ScanState.Done:
                case VpsScanManager.ScanState.Error:
                    if (GUILayout.Button("Start Scan", _buttonStyle, GUILayout.Width(btnW), GUILayout.Height(btnH)))
                        scanManager.StartScan();
                    break;

                case VpsScanManager.ScanState.Scanning:
                    if (GUILayout.Button("Stop & Upload (" + scanManager.CapturedFrames + ")",
                        _buttonActiveStyle, GUILayout.Width(btnW), GUILayout.Height(btnH)))
                        scanManager.StopScanAndUpload();

                    GUILayout.Space(4);
                    if (GUILayout.Button("Cancel", _buttonStyle, GUILayout.Width(btnW), GUILayout.Height(40f)))
                        scanManager.CancelScan();
                    break;

                case VpsScanManager.ScanState.Packaging:
                case VpsScanManager.ScanState.Uploading:
                case VpsScanManager.ScanState.Building:
                    GUILayout.Label("<color=#FFAA00>Processing...</color>", _labelStyle);
                    break;
            }

            // After successful build, offer to switch to new map for localization
            if (state == VpsScanManager.ScanState.Done && !string.IsNullOrEmpty(scanManager.CurrentMapId))
            {
                GUILayout.Space(4);
                if (GUILayout.Button("Use This Map", _buttonActiveStyle, GUILayout.Width(btnW), GUILayout.Height(btnH)))
                {
                    if (vpsClient != null)
                    {
                        vpsClient.MapId = scanManager.CurrentMapId;
                        _scanMode = false;
                        if (arPoseHandler != null)
                        {
                            arPoseHandler.PausedForScan = false;
                            arPoseHandler.StartCapturing();
                        }
                    }
                }
            }

            GUILayout.EndArea();
        }

        private string GetStateColor(VpsScanManager.ScanState s)
        {
            return s switch
            {
                VpsScanManager.ScanState.Idle => "#AAAAAA",
                VpsScanManager.ScanState.Scanning => "#00FF88",
                VpsScanManager.ScanState.Packaging => "#FFAA00",
                VpsScanManager.ScanState.Uploading => "#FFAA00",
                VpsScanManager.ScanState.Building => "#88CCFF",
                VpsScanManager.ScanState.Done => "#00FF88",
                VpsScanManager.ScanState.Error => "#FF4444",
                _ => "#FFFFFF"
            };
        }

        private void InitStyles()
        {
            if (_stylesInit) return;
            _stylesInit = true;

            _boxStyle = new GUIStyle(GUI.skin.box);
            _boxStyle.normal.background = MakeTex(2, 2, new Color(0.05f, 0.05f, 0.1f, 0.85f));

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = fontSize;
            _labelStyle.normal.textColor = Color.white;
            _labelStyle.richText = true;
            _labelStyle.wordWrap = true;

            _headerStyle = new GUIStyle(GUI.skin.label);
            _headerStyle.fontSize = fontSize + 4;
            _headerStyle.fontStyle = FontStyle.Bold;
            _headerStyle.normal.textColor = new Color(1f, 0.7f, 0.3f);

            _buttonStyle = new GUIStyle(GUI.skin.button);
            _buttonStyle.fontSize = fontSize;
            _buttonStyle.normal.textColor = Color.white;

            _buttonActiveStyle = new GUIStyle(GUI.skin.button);
            _buttonActiveStyle.fontSize = fontSize;
            _buttonActiveStyle.fontStyle = FontStyle.Bold;
            _buttonActiveStyle.normal.textColor = new Color(0.3f, 1f, 0.5f);
            _buttonActiveStyle.normal.background = MakeTex(2, 2, new Color(0.1f, 0.3f, 0.15f, 0.9f));
        }

        private Texture2D MakeTex(int w, int h, Color c)
        {
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = c;
            var t = new Texture2D(w, h);
            t.SetPixels(pix);
            t.Apply();
            return t;
        }
    }
}
