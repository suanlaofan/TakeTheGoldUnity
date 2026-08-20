using System.Collections.Generic;
using UnityEngine;

namespace Wukong.StackingHouseVR
{
    [DisallowMultipleComponent]
    public sealed class StackingHouseGameController : MonoBehaviour
    {
        [SerializeField] private Transform foundationTop;
        [SerializeField] private PlayerElevatorController playerElevator;
        [SerializeField, Min(0.05f)] private float stabilityCheckInterval = 0.15f;
        [SerializeField, Min(0.1f)] private float fallenHouseDepth = 1.5f;
        [SerializeField, Min(1f)] private float playAreaRadius = 6f;

        private readonly HashSet<StackableHouse> activeHouses = new HashSet<StackableHouse>();
        private float nextStabilityCheckTime;
        private float highestStableTop;

        public float FoundationTopY => foundationTop != null ? foundationTop.position.y : transform.position.y;
        public float HighestStableTop => highestStableTop;
        public int StableHouseCount { get; private set; }

        public void Configure(Transform newFoundationTop, PlayerElevatorController newPlayerElevator)
        {
            foundationTop = newFoundationTop;
            playerElevator = newPlayerElevator;
            highestStableTop = FoundationTopY;
        }

        public void RegisterHouse(StackableHouse house)
        {
            if (house == null)
                return;

            activeHouses.Add(house);
            house.AssignGame(this);
        }

        public void UnregisterHouse(StackableHouse house)
        {
            if (house == null)
                return;

            activeHouses.Remove(house);
        }

        public void NotifyHouseStateChanged()
        {
            nextStabilityCheckTime = 0f;
        }

        private void Awake()
        {
            highestStableTop = FoundationTopY;
        }

        private void Update()
        {
            if (Time.time < nextStabilityCheckTime)
                return;

            nextStabilityCheckTime = Time.time + stabilityCheckInterval;
            RecalculateTower();
        }

        private void RecalculateTower()
        {
            float newHighestTop = FoundationTopY;
            List<StackableHouse> fallenHouses = null;
            List<StackableHouse> stableCandidates = new List<StackableHouse>();

            foreach (StackableHouse house in activeHouses)
            {
                if (house == null || house.IsOnTray || house.IsSelected)
                    continue;

                Vector3 offset = house.transform.position - foundationTop.position;
                bool isOutsidePlayArea = new Vector2(offset.x, offset.z).sqrMagnitude > playAreaRadius * playAreaRadius;
                bool isBelowFoundation = house.transform.position.y < FoundationTopY - fallenHouseDepth;
                if (isOutsidePlayArea || isBelowFoundation)
                {
                    fallenHouses ??= new List<StackableHouse>();
                    fallenHouses.Add(house);
                    continue;
                }

                if (!house.IsStable)
                    continue;

                stableCandidates.Add(house);
            }

            if (fallenHouses != null)
            {
                foreach (StackableHouse house in fallenHouses)
                {
                    activeHouses.Remove(house);
                    Destroy(house.gameObject);
                }
            }

            Transform foundationRoot = foundationTop != null && foundationTop.parent != null
                ? foundationTop.parent
                : foundationTop;
            HashSet<StackableHouse> connectedHouses = new HashSet<StackableHouse>();
            bool foundConnection;
            do
            {
                foundConnection = false;
                foreach (StackableHouse house in stableCandidates)
                {
                    if (connectedHouses.Contains(house) || !house.HasConnectedSupport(foundationRoot, connectedHouses))
                        continue;

                    connectedHouses.Add(house);
                    foundConnection = true;
                }
            }
            while (foundConnection);

            foreach (StackableHouse house in connectedHouses)
                newHighestTop = Mathf.Max(newHighestTop, house.WorldBounds.max.y);

            StableHouseCount = connectedHouses.Count;
            highestStableTop = newHighestTop;

            if (playerElevator != null)
                playerElevator.SetTowerTop(highestStableTop, StackableHouse.AnyHouseSelected);
        }
    }
}
