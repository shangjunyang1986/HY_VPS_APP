using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace VPS
{
    [Serializable]
    public class V2Error
    {
        public string code;
        public string message;
        public string detail;
        public string field;
    }

    [Serializable]
    public class V2ErrorResponse
    {
        public bool success;
        public V2Error error;
    }

    [Serializable]
    public class CaptureCreateResp
    {
        public bool success;
        public string capture_id;
        public string status;
    }

    [Serializable]
    public class CaptureUploadResp
    {
        public bool success;
        public string capture_id;
        public string status;
        public string[] uploaded_files;
    }

    [Serializable]
    public class MapCreateResp
    {
        public bool success;
        public string map_id;
        public string status;
        public string build_status;
        public string calibration_status;
    }

    [Serializable]
    public class MapStatusResp
    {
        public bool success;
        public string map_id;
        public string status;
        public string build_status;
        public string calibration_status;
        public bool ready_for_localization;
        public bool metric_aligned;
    }

    [Serializable]
    internal class MapStatusListWrapper
    {
        public MapStatusResp[] items;
    }

    /// <summary>
    /// HTTP client for VPS v2 API — handles capture creation, upload, and map building.
    /// </summary>
    public class VpsScanClient : MonoBehaviour
    {
        [Header("Server")]
        [SerializeField] private string serverUrl = "http://192.168.1.8:8001";

        public string ServerUrl { get => serverUrl; set => serverUrl = value; }

        public event Action<string> OnCaptureCreated;
        public event Action<string> OnUploadComplete;
        public event Action<string, string> OnUploadFailed;
        public event Action<string> OnMapCreated;
        public event Action<MapStatusResp> OnMapStatusUpdated;

        public IEnumerator CreateCapture(string deviceModel, Action<string> callback = null)
        {
            string url = serverUrl + "/api/v2/captures";
            string body = "{\"device_model\":\"" + deviceModel + "\",\"platform\":\"android\"}";

            var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var resp = JsonUtility.FromJson<CaptureCreateResp>(req.downloadHandler.text);
                if (resp == null || string.IsNullOrEmpty(resp.capture_id))
                {
                    Debug.LogError("[ScanClient] CreateCapture: invalid response");
                    callback?.Invoke(null);
                }
                else
                {
                    Debug.Log("[ScanClient] Capture created: " + resp.capture_id);
                    OnCaptureCreated?.Invoke(resp.capture_id);
                    callback?.Invoke(resp.capture_id);
                }
            }
            else
            {
                Debug.LogError("[ScanClient] CreateCapture failed: " + req.error);
                callback?.Invoke(null);
            }

            req.Dispose();
        }

        public IEnumerator UploadCapture(
            string captureId,
            string framesZipPath,
            float fx, float fy, float cx, float cy,
            int imageWidth, int imageHeight, int orientation,
            string deviceModel,
            string vioTrajectoryPath = null)
        {
            string url = serverUrl + "/api/v2/captures/" + captureId + "/upload";

            string intrJson = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{{\"fx\":{0},\"fy\":{1},\"cx\":{2},\"cy\":{3},\"width\":{4},\"height\":{5},\"orientation\":{6}}}",
                fx, fy, cx, cy, imageWidth, imageHeight, orientation);

            string metaJson = "{\"device_model\":\"" + deviceModel + "\",\"capture_type\":\"scan\"}";

            byte[] zipBytes = File.ReadAllBytes(framesZipPath);

            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("video", zipBytes, "frames.zip", "application/zip"),
                new MultipartFormDataSection("intrinsics_json", intrJson),
                new MultipartFormDataSection("capture_meta_json", metaJson)
            };

            // Attach VIO trajectory if available
            if (!string.IsNullOrEmpty(vioTrajectoryPath) && File.Exists(vioTrajectoryPath))
            {
                byte[] vioBytes = File.ReadAllBytes(vioTrajectoryPath);
                form.Add(new MultipartFormFileSection("vio_trajectory", vioBytes, "vio_trajectory.json", "application/json"));
                Debug.Log("[ScanClient] Including VIO trajectory (" + vioBytes.Length + " bytes)");
            }

            var req = UnityWebRequest.Post(url, form);
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("[ScanClient] Upload complete for " + captureId);
                OnUploadComplete?.Invoke(captureId);
            }
            else
            {
                string errBody = req.downloadHandler != null ? req.downloadHandler.text : req.error;
                Debug.LogError("[ScanClient] Upload failed: " + errBody);

                string errMsg = errBody;
                try
                {
                    var errResp = JsonUtility.FromJson<V2ErrorResponse>(errBody);
                    if (errResp != null && errResp.error != null)
                        errMsg = errResp.error.code + ": " + errResp.error.message;
                }
                catch (Exception) { }

                OnUploadFailed?.Invoke(captureId, errMsg);
            }

            req.Dispose();
        }

        public IEnumerator CreateMap(string captureId, bool autoCalibrate, Action<string> callback = null)
        {
            string url = serverUrl + "/api/v2/maps";
            string body = "{\"capture_id\":\"" + captureId + "\",\"auto_calibrate\":" +
                           (autoCalibrate ? "true" : "false") + "}";

            var req = new UnityWebRequest(url, "POST");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var resp = JsonUtility.FromJson<MapCreateResp>(req.downloadHandler.text);
                if (resp == null || string.IsNullOrEmpty(resp.map_id))
                {
                    Debug.LogError("[ScanClient] CreateMap: invalid response");
                    callback?.Invoke(null);
                }
                else
                {
                    Debug.Log("[ScanClient] Map created: " + resp.map_id);
                    OnMapCreated?.Invoke(resp.map_id);
                    callback?.Invoke(resp.map_id);
                }
            }
            else
            {
                Debug.LogError("[ScanClient] CreateMap failed: " + req.error);
                callback?.Invoke(null);
            }

            req.Dispose();
        }

        public IEnumerator QueryMapStatus(string mapId, Action<MapStatusResp> callback = null)
        {
            string url = serverUrl + "/api/v2/maps/" + mapId;

            var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                var resp = JsonUtility.FromJson<MapStatusResp>(req.downloadHandler.text);
                if (resp == null)
                {
                    Debug.LogError("[ScanClient] QueryMapStatus: invalid response");
                    callback?.Invoke(null);
                }
                else
                {
                    OnMapStatusUpdated?.Invoke(resp);
                    callback?.Invoke(resp);
                }
            }
            else
            {
                Debug.LogError("[ScanClient] QueryMapStatus failed: " + req.error);
                callback?.Invoke(null);
            }

            req.Dispose();
        }

        /// <summary>
        /// List all maps registered in the v2 API. Returns null on failure.
        /// </summary>
        public IEnumerator ListMaps(Action<MapStatusResp[]> callback)
        {
            string url = serverUrl + "/api/v2/maps";

            var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                string body = req.downloadHandler.text;
                MapStatusResp[] parsed = null;
                try
                {
                    // JsonUtility cannot parse top-level arrays — wrap it.
                    string wrapped = "{\"items\":" + body + "}";
                    var wrapper = JsonUtility.FromJson<MapStatusListWrapper>(wrapped);
                    parsed = wrapper != null ? wrapper.items : null;
                }
                catch (Exception e)
                {
                    Debug.LogError("[ScanClient] ListMaps parse failed: " + e.Message);
                }
                callback?.Invoke(parsed);
            }
            else
            {
                Debug.LogError("[ScanClient] ListMaps failed: " + req.error);
                callback?.Invoke(null);
            }

            req.Dispose();
        }

        public IEnumerator PollMapStatus(string mapId, float intervalSec = 3f)
        {
            while (true)
            {
                MapStatusResp status = null;
                yield return QueryMapStatus(mapId, s => status = s);

                if (status == null) yield break;

                string bs = status.build_status != null ? status.build_status : "";
                string st = status.status != null ? status.status : "";
                if (bs == "succeeded" || bs == "failed" || st == "ready" || st == "failed")
                    yield break;

                yield return new WaitForSeconds(intervalSec);
            }
        }
    }
}
