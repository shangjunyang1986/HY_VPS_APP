using UnityEngine;

namespace VPS
{
    /// <summary>
    /// VPS Anchor - places/updates a virtual object based on VPS world-coordinate pose.
    /// Supports smooth interpolation to avoid jumps.
    /// </summary>
    public class VpsAnchor : MonoBehaviour
    {
        [Header("Anchor Settings")]
        [SerializeField] private Vector3 worldPosition;
        [SerializeField] private Quaternion worldRotation = Quaternion.identity;

        [Header("Smoothing")]
        [SerializeField] private bool enableSmoothing = true;
        [SerializeField] private float positionSmoothSpeed = 8f;
        [SerializeField] private float rotationSmoothSpeed = 8f;

        [Header("Status")]
        [SerializeField] private bool isAnchored = false;

        private Vector3 _targetPosition;
        private Quaternion _targetRotation;

        public bool IsAnchored => isAnchored;
        public Vector3 WorldPosition => worldPosition;

        /// <summary>
        /// Set anchor pose from VPS world coordinates
        /// </summary>
        public void SetPose(Vector3 position, Quaternion rotation)
        {
            worldPosition = position;
            worldRotation = rotation;
            _targetPosition = position;
            _targetRotation = rotation;

            if (!isAnchored)
            {
                // First placement: snap immediately
                transform.SetPositionAndRotation(position, rotation);
                isAnchored = true;
            }
        }

        /// <summary>
        /// Set anchor pose from VpsPose data
        /// </summary>
        public void SetPose(VpsPose pose)
        {
            if (pose == null || pose.position == null || pose.position.Length < 3) return;

            Vector3 pos = new Vector3(pose.position[0], pose.position[1], pose.position[2]);

            Quaternion rot = Quaternion.identity;
            if (pose.rotation != null && pose.rotation.Length >= 4)
            {
                // VPS: [qw, qx, qy, qz] -> Unity: Quaternion(qx, qy, qz, qw)
                rot = new Quaternion(pose.rotation[1], pose.rotation[2], pose.rotation[3], pose.rotation[0]);
            }

            SetPose(pos, rot);
        }

        private void Update()
        {
            if (!isAnchored || !enableSmoothing) return;

            float dt = Time.deltaTime;
            transform.position = Vector3.Lerp(transform.position, _targetPosition, positionSmoothSpeed * dt);
            transform.rotation = Quaternion.Slerp(transform.rotation, _targetRotation, rotationSmoothSpeed * dt);
        }

        /// <summary>
        /// Reset anchor state
        /// </summary>
        public void ResetAnchor()
        {
            isAnchored = false;
        }
    }
}
