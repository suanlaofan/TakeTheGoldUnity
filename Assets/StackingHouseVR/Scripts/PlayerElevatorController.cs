using UnityEngine;

namespace Wukong.StackingHouseVR
{
    [DisallowMultipleComponent]
    public sealed class PlayerElevatorController : MonoBehaviour
    {
        [SerializeField] private Transform movingPlatform;
        [SerializeField] private Transform leftGuideColumn;
        [SerializeField] private Transform rightGuideColumn;
        [SerializeField, Min(0.5f)] private float activationHeight = 1.65f;
        [SerializeField, Min(0.5f)] private float interactionHeight = 1.25f;
        [SerializeField, Min(0.05f)] private float riseSpeed = 0.45f;
        [SerializeField, Min(0f)] private float maximumRise = 12f;

        private float targetRise;

        public float CurrentRise => movingPlatform != null ? movingPlatform.localPosition.y : 0f;

        public void Configure(Transform platform, Transform leftColumn, Transform rightColumn)
        {
            movingPlatform = platform;
            leftGuideColumn = leftColumn;
            rightGuideColumn = rightColumn;
            targetRise = 0f;
            RefreshGuideColumns();
        }

        public void SetTowerTop(float towerTopWorldY, bool pauseMovement)
        {
            if (movingPlatform == null || pauseMovement)
                return;

            float baseWorldY = transform.position.y;
            if (towerTopWorldY <= baseWorldY + activationHeight)
            {
                targetRise = 0f;
                return;
            }

            float requestedRise = towerTopWorldY - baseWorldY - interactionHeight;
            targetRise = Mathf.Clamp(requestedRise, 0f, maximumRise);
        }

        private void Update()
        {
            if (movingPlatform == null || StackableHouse.AnyHouseSelected)
                return;

            Vector3 localPosition = movingPlatform.localPosition;
            float nextRise = Mathf.MoveTowards(localPosition.y, targetRise, riseSpeed * Time.deltaTime);
            if (Mathf.Approximately(localPosition.y, nextRise))
                return;

            localPosition.y = nextRise;
            movingPlatform.localPosition = localPosition;
            RefreshGuideColumns();
        }

        private void RefreshGuideColumns()
        {
            float rise = CurrentRise;
            UpdateGuideColumn(leftGuideColumn, rise);
            UpdateGuideColumn(rightGuideColumn, rise);
        }

        private static void UpdateGuideColumn(Transform column, float rise)
        {
            if (column == null)
                return;

            Vector3 scale = column.localScale;
            scale.y = Mathf.Max(0.35f, rise + 0.35f);
            column.localScale = scale;

            Vector3 position = column.localPosition;
            position.y = scale.y * 0.5f;
            column.localPosition = position;
        }
    }
}
