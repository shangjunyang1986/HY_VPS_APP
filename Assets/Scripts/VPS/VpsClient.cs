using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace VPS
{
    [Serializable]
    public class VpsPose
    {
        public float[] position;
        public float[] rotation;
        public float timestamp;
        public float confidence;
    }

    [Serializable]
    public class SessionFrameResponse
    {
        public bool success;
        public VpsPose pose;
        public VpsPose fused_pose;
        public float confidence;
        public string tracking_state;
        public string quality_level;
        public int num_inliers;
        public float latency_ms;
        public string failure_reason;
    }

    [Serializable]
    public class SessionStartResponse
    {
        public string session_id;
        public string map_id;
        public string tracking_state;
    }

    [Serializable]
    public class SessionStartRequest
    {
        public string map_id;
        public string config;
    }

    [Serializable]
    public class VioPoseInput
    {
        public float[] position;
        public float[] rotation;
        public float timestamp;
    }

    [Serializable]
    public class SessionFrameRequest
    {
        public string session_id;
        public string image_base64;
        public float timestamp;
        public Intrinsics intrinsics;
        public VioPoseInput vio_pose;
    }

    [Serializable]
    public class Intrinsics
    {
        public float fx;
        public float fy;
        public float cx;
        public float cy;
    }

    /// <summary>
    /// VPS Client - connects to VPS backend API for visual positioning
    /// </summary>
    public class VpsClient : MonoBehaviour
    {
        [Header("VPS Settings")]
        [SerializeField] private string serverUrl = "http://192.168.1.8:8000";
        [SerializeField] private string mapId = "default_map";

        [Header("Status")]
        [SerializeField] private string sessionId = "";
        [SerializeField] private string trackingState = "INIT";
        [SerializeField] private bool isConnected = false;
        [SerializeField] private float lastLatencyMs = 0f;

        public string ServerUrl { get => serverUrl; set => serverUrl = value; }
        public string MapId { get => mapId; set => mapId = value; }
        public string SessionId => sessionId;
        public string TrackingState => trackingState;
        public bool IsConnected => isConnected;
        public float LastLatencyMs => lastLatencyMs;

        public event Action<VpsPose> OnPoseReceived;
        public event Action<VpsPose> OnFusedPoseReceived;
        public event Action<string> OnTrackingStateChanged;

        private UnityWebRequest CreatePostRequest(string url, string jsonBody)
        {
            var req = new UnityWebRequest(url, "POST");
            byte[] raw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
            req.uploadHandler = new UploadHandlerRaw(raw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            return req;
        }

        /// <summary>
        /// Start a new VPS session
        /// </summary>
        public IEnumerator StartSession()
        {
            var url = serverUrl + "/session/start";
            Debug.LogError($"[VPS] StartSession: url={url}");
            var requestData = new SessionStartRequest { map_id = mapId, config = "" };
            var jsonData = JsonUtility.ToJson(requestData);

            UnityWebRequest request = null;
            try
            {
                request = CreatePostRequest(url, jsonData);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[VPS] CreatePostRequest exception: {e.Message}");
                yield break;
            }

            Debug.LogError("[VPS] StartSession: sending request...");
            using (request)
            {
            yield return request.SendWebRequest();

            Debug.LogError($"[VPS] StartSession response: result={request.result}, code={request.responseCode}, error={request.error}");
            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<SessionStartResponse>(request.downloadHandler.text);
                sessionId = response.session_id;
                trackingState = response.tracking_state;
                isConnected = true;
                OnTrackingStateChanged?.Invoke(trackingState);
                Debug.LogError($"[VPS] Session started: {sessionId}");
            }
            else
            {
                Debug.LogError($"[VPS] Failed to start session: {request.error}, body={request.downloadHandler?.text}");
                isConnected = false;
            }
            } // end using(request)
        }

        /// <summary>
        /// Send a frame to VPS for localization
        /// </summary>
        public IEnumerator SendFrame(Texture2D image, float timestamp, Intrinsics intrinsics, VioPoseInput vioPose = null)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError("[VPS] No session ID. Call StartSession first.");
                yield break;
            }

            var url = serverUrl + "/session/frame";

            string base64Image = "";
            if (image != null)
            {
                byte[] imageData = image.EncodeToJPG(75);
                base64Image = Convert.ToBase64String(imageData);
            }

            var frameData = new SessionFrameRequest
            {
                session_id = sessionId,
                image_base64 = base64Image,
                timestamp = timestamp,
                intrinsics = intrinsics,
                vio_pose = vioPose
            };

            var jsonData = JsonUtility.ToJson(frameData);
            Debug.Log($"[VPS] Sending frame to {url} (image={base64Image.Length} chars, json={jsonData.Length} chars)");
            using var request = CreatePostRequest(url, jsonData);
            yield return request.SendWebRequest();
            Debug.Log($"[VPS] Frame response: {request.result}, code={request.responseCode}");

            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<SessionFrameResponse>(request.downloadHandler.text);

                if (response.success)
                {
                    lastLatencyMs = response.latency_ms;
                    string prevState = trackingState;
                    trackingState = response.tracking_state;

                    if (prevState != trackingState)
                        OnTrackingStateChanged?.Invoke(trackingState);

                    if (response.pose != null)
                        OnPoseReceived?.Invoke(response.pose);

                    if (response.fused_pose != null)
                        OnFusedPoseReceived?.Invoke(response.fused_pose);
                }
                else
                {
                    Debug.LogWarning($"[VPS] Frame failed: {response.failure_reason}");
                }
            }
            else
            {
                Debug.LogError($"[VPS] Send frame failed: {request.error}");
            }
        }

        /// <summary>
        /// End the VPS session
        /// </summary>
        public IEnumerator EndSession()
        {
            if (string.IsNullOrEmpty(sessionId)) yield break;

            var url = serverUrl + "/session/end";
            var jsonData = $"{{\"session_id\":\"{sessionId}\"}}";

            using var request = CreatePostRequest(url, jsonData);
            yield return request.SendWebRequest();

            sessionId = "";
            trackingState = "INIT";
            isConnected = false;
            OnTrackingStateChanged?.Invoke(trackingState);
            Debug.Log("[VPS] Session ended");
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
        }

        private void OnApplicationQuit()
        {
            StartCoroutine(EndSession());
        }
    }
}
