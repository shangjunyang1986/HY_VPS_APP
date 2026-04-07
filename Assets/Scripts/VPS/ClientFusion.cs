using UnityEngine;

namespace VPS
{
    /// <summary>
    /// Client-side fusion: applies VPS offset to VIO poses for 60fps smooth positioning.
    /// Mirrors backend FusionService logic (offset-based).
    /// </summary>
    public class ClientFusion
    {
        private Vector3 _positionOffset;
        private Quaternion _rotationOffset = Quaternion.identity;
        private Vector3 _targetPositionOffset;
        private Quaternion _targetRotationOffset = Quaternion.identity;
        private bool _hasOffset;
        private float _smoothSpeed;

        public bool HasOffset => _hasOffset;
        public Vector3 PositionOffset => _positionOffset;

        public ClientFusion(float smoothSpeed = 8.0f)
        {
            _smoothSpeed = smoothSpeed;
        }

        /// <summary>
        /// Update offset when VPS succeeds. Offset = VPS world pose - VIO local pose.
        /// </summary>
        public void UpdateOffset(Vector3 vpsWorldPos, Quaternion vpsWorldRot, Vector3 vioLocalPos, Quaternion vioLocalRot)
        {
            _targetPositionOffset = vpsWorldPos - vioLocalPos;
            _targetRotationOffset = vpsWorldRot * Quaternion.Inverse(vioLocalRot);

            if (!_hasOffset)
            {
                // First time: snap immediately
                _positionOffset = _targetPositionOffset;
                _rotationOffset = _targetRotationOffset;
                _hasOffset = true;
            }
            // Otherwise, smooth interpolation happens in ApplyFusion
        }

        /// <summary>
        /// Call every frame. Returns fused world pose from current VIO pose + offset.
        /// </summary>
        public (Vector3 position, Quaternion rotation) ApplyFusion(Vector3 vioPos, Quaternion vioRot, float deltaTime)
        {
            if (!_hasOffset)
                return (vioPos, vioRot);

            // Smooth offset interpolation toward target
            float t = 1f - Mathf.Exp(-_smoothSpeed * deltaTime);
            _positionOffset = Vector3.Lerp(_positionOffset, _targetPositionOffset, t);
            _rotationOffset = Quaternion.Slerp(_rotationOffset, _targetRotationOffset, t);

            Vector3 fusedPos = vioPos + _positionOffset;
            Quaternion fusedRot = _rotationOffset * vioRot;

            return (fusedPos, fusedRot);
        }

        public void Reset()
        {
            _hasOffset = false;
            _positionOffset = Vector3.zero;
            _rotationOffset = Quaternion.identity;
            _targetPositionOffset = Vector3.zero;
            _targetRotationOffset = Quaternion.identity;
        }
    }
}
