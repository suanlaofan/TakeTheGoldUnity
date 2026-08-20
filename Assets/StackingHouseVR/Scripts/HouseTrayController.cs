using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Wukong.StackingHouseVR
{
    [DisallowMultipleComponent]
    public sealed class HouseTrayController : MonoBehaviour
    {
        [SerializeField] private StackingHouseGameController game;
        [SerializeField] private GameObject housePrefab;
        [SerializeField] private List<Transform> slots = new List<Transform>();
        [SerializeField, Min(0f)] private float refillDelay = 0.45f;

        private StackableHouse[] stockedHouses;

        public void Configure(StackingHouseGameController newGame, GameObject newHousePrefab, IEnumerable<Transform> newSlots)
        {
            game = newGame;
            housePrefab = newHousePrefab;
            slots = new List<Transform>(newSlots);
            stockedHouses = new StackableHouse[slots.Count];
        }

        public void SetInitialHouse(int slotIndex, StackableHouse house)
        {
            EnsureStockArray();
            if (slotIndex < 0 || slotIndex >= stockedHouses.Length || house == null)
                return;

            stockedHouses[slotIndex] = house;
            PrepareHouseForSlot(house, slotIndex);
        }

        public void NotifyHouseTaken(StackableHouse house, int slotIndex)
        {
            EnsureStockArray();
            if (slotIndex < 0 || slotIndex >= stockedHouses.Length || stockedHouses[slotIndex] != house)
                return;

            stockedHouses[slotIndex] = null;
            StartCoroutine(RefillSlot(slotIndex));
        }

        private void Awake()
        {
            EnsureStockArray();

            for (int index = 0; index < slots.Count; index++)
            {
                if (stockedHouses[index] == null && slots[index] != null)
                    stockedHouses[index] = slots[index].GetComponentInChildren<StackableHouse>(true);

                if (stockedHouses[index] != null)
                    PrepareHouseForSlot(stockedHouses[index], index);

                if (stockedHouses[index] == null)
                    SpawnHouse(index);
            }
        }

        private IEnumerator RefillSlot(int slotIndex)
        {
            yield return new WaitForSeconds(refillDelay);
            SpawnHouse(slotIndex);
        }

        private void SpawnHouse(int slotIndex)
        {
            EnsureStockArray();
            if (housePrefab == null || slotIndex < 0 || slotIndex >= slots.Count || stockedHouses[slotIndex] != null)
                return;

            GameObject instance = Instantiate(housePrefab, slots[slotIndex]);
            instance.name = $"House Ready {slotIndex + 1}";
            StackableHouse house = instance.GetComponent<StackableHouse>();
            stockedHouses[slotIndex] = house;
            PrepareHouseForSlot(house, slotIndex);
        }

        private void PrepareHouseForSlot(StackableHouse house, int slotIndex)
        {
            if (house == null || slotIndex < 0 || slotIndex >= slots.Count)
                return;

            Transform slot = slots[slotIndex];
            house.transform.SetParent(slot, false);
            house.transform.localPosition = Vector3.zero;
            house.transform.localRotation = Quaternion.identity;
            house.AssignGame(game);
            house.PlaceOnTray(this, slotIndex);
            game?.RegisterHouse(house);
        }

        private void EnsureStockArray()
        {
            if (stockedHouses == null || stockedHouses.Length != slots.Count)
                stockedHouses = new StackableHouse[slots.Count];
        }
    }
}
