using UnityEngine;

namespace VPS
{
    /// <summary>
    /// Controls when to trigger VPS requests based on tracking state and drift estimation.
    /// State machine: INIT -> VPS_LOCKED -> VPS_DEGRADED -> RELOCALIZING
    /// </summary>
    public class VpsTriggerController
    {
        [System.Serializable]
        public class Settings
        {
            public float normalInterval = 2.0f;
            public float fastInterval = 1.0f;
            public float driftThreshold = 0.5f;
            public int maxConsecutiveFailures = 3;
        }

        private readonly Settings _settings;
        private string _trackingState = "INIT";
        private float _timeSinceLastTrigger;
        private int _consecutiveFailures;
        private Vector3 _lastVpsVioPosition;
        private bool _hasVpsAnchor;
        private float _estimatedDrift;

        public string TrackingState => _trackingState;
        public float CurrentInterval => GetCurrentInterval();
        public float EstimatedDrift => _estimatedDrift;
        public int ConsecutiveFailures => _consecutiveFailures;

        public VpsTriggerController(Settings settings = null)
        {
            _settings = settings ?? new Settings();
        }

        /// <summary>
        /// Call every frame. Returns true when it's time to send a VPS request.
        /// </summary>
        public bool ShouldTriggerVps(float deltaTime, Vector3 currentVioPosition)
        {
            _timeSinceLastTrigger += deltaTime;

            // Update drift estimate
            if (_hasVpsAnchor)
                _estimatedDrift = Vector3.Distance(currentVioPosition, _lastVpsVioPosition);

            float interval = GetCurrentInterval();
            if (_timeSinceLastTrigger >= interval)
            {
                _timeSinceLastTrigger = 0f;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Call when a VPS response is received.
        /// </summary>
        public void OnVpsResponse(string serverTrackingState, bool success, float confidence, Vector3 vioPositionAtCapture)
        {
            if (success)
            {
                _consecutiveFailures = 0;
                _lastVpsVioPosition = vioPositionAtCapture;
                _hasVpsAnchor = true;
                _estimatedDrift = 0f;
            }
            else
            {
                _consecutiveFailures++;
            }

            // Update tracking state from server
            if (!string.IsNullOrEmpty(serverTrackingState))
                _trackingState = serverTrackingState;

            // Client-side override: too many failures -> DEGRADED
            if (_consecutiveFailures >= _settings.maxConsecutiveFailures && _trackingState == "VPS_LOCKED")
                _trackingState = "VPS_DEGRADED";
        }

        public void Reset()
        {
            _trackingState = "INIT";
            _timeSinceLastTrigger = 0f;
            _consecutiveFailures = 0;
            _hasVpsAnchor = false;
            _estimatedDrift = 0f;
        }

        private float GetCurrentInterval()
        {
            // Fast mode: INIT, DEGRADED, RELOCALIZING, or high drift
            bool useFast = _trackingState != "VPS_LOCKED"
                           || (_hasVpsAnchor && _estimatedDrift > _settings.driftThreshold);

            return useFast ? _settings.fastInterval : _settings.normalInterval;
        }
    }
}
