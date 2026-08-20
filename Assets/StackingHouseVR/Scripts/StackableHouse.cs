using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Wukong.StackingHouseVR
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(Collider), typeof(XRGrabInteractable))]
    public sealed class StackableHouse : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float settleDuration = 0.8f;
        [SerializeField, Min(0.01f)] private float stableLinearSpeed = 0.08f;
        [SerializeField, Min(0.01f)] private float stableAngularSpeed = 0.18f;

        private Rigidbody body;
        private Collider houseCollider;
        private XRGrabInteractable grabInteractable;
        private StackingHouseGameController game;
        private HouseTrayController sourceTray;
        private int traySlotIndex = -1;
        private readonly HashSet<Collider> supportingColliders = new HashSet<Collider>();
        private float stableTimer;
        private bool hasBeenReleased;
        private bool countedAsSelected;

        public static int SelectedHouseCount { get; private set; }
        public static bool AnyHouseSelected => SelectedHouseCount > 0;

        public bool IsOnTray { get; private set; }
        public bool IsStable { get; private set; }
        public bool IsSelected => grabInteractable != null && grabInteractable.isSelected;
        public Bounds WorldBounds => houseCollider != null ? houseCollider.bounds : new Bounds(transform.position, Vector3.zero);

        public bool HasConnectedSupport(Transform foundationRoot, ISet<StackableHouse> connectedHouses)
        {
            supportingColliders.RemoveWhere(collider => collider == null);
            foreach (Collider supportCollider in supportingColliders)
            {
                if (foundationRoot != null && supportCollider.transform.IsChildOf(foundationRoot))
                    return true;

                StackableHouse supportingHouse = supportCollider.GetComponentInParent<StackableHouse>();
                if (supportingHouse != null && supportingHouse != this && connectedHouses.Contains(supportingHouse))
                    return true;
            }

            return false;
        }

        public void AssignGame(StackingHouseGameController newGame)
        {
            game = newGame;
        }

        public void PlaceOnTray(HouseTrayController tray, int slotIndex)
        {
            sourceTray = tray;
            traySlotIndex = slotIndex;
            IsOnTray = true;
            IsStable = false;
            hasBeenReleased = false;
            stableTimer = 0f;
            supportingColliders.Clear();

            EnsureReferences();
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.isKinematic = true;
            body.useGravity = false;
        }

        private void Awake()
        {
            EnsureReferences();
        }

        private void OnEnable()
        {
            EnsureReferences();
            grabInteractable.selectEntered.AddListener(OnSelected);
            grabInteractable.selectExited.AddListener(OnReleased);
        }

        private void OnDisable()
        {
            if (countedAsSelected)
            {
                SelectedHouseCount = Mathf.Max(0, SelectedHouseCount - 1);
                countedAsSelected = false;
            }

            supportingColliders.Clear();

            if (grabInteractable == null)
                return;

            grabInteractable.selectEntered.RemoveListener(OnSelected);
            grabInteractable.selectExited.RemoveListener(OnReleased);
        }

        private void FixedUpdate()
        {
            if (!hasBeenReleased || IsOnTray || IsSelected || body.isKinematic)
                return;

            supportingColliders.RemoveWhere(collider => collider == null);
            bool isSupported = supportingColliders.Count > 0;
            bool isSlow = body.IsSleeping() ||
                          (body.linearVelocity.sqrMagnitude <= stableLinearSpeed * stableLinearSpeed &&
                           body.angularVelocity.sqrMagnitude <= stableAngularSpeed * stableAngularSpeed);

            if (isSupported && isSlow)
            {
                stableTimer += Time.fixedDeltaTime;
                if (!IsStable && stableTimer >= settleDuration)
                {
                    IsStable = true;
                    game?.NotifyHouseStateChanged();
                }
            }
            else
            {
                stableTimer = 0f;
                if (IsStable)
                {
                    IsStable = false;
                    game?.NotifyHouseStateChanged();
                }
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            RefreshSupport(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            RefreshSupport(collision);
        }

        private void OnCollisionExit(Collision collision)
        {
            supportingColliders.Remove(collision.collider);
        }

        private void RefreshSupport(Collision collision)
        {
            if (IsOnTray || IsSelected)
                return;

            bool hasUpwardContact = false;
            foreach (ContactPoint contact in collision.contacts)
            {
                if (contact.normal.y > 0.45f)
                {
                    hasUpwardContact = true;
                    break;
                }
            }

            if (hasUpwardContact)
                supportingColliders.Add(collision.collider);
            else
                supportingColliders.Remove(collision.collider);
        }

        private void OnSelected(SelectEnterEventArgs args)
        {
            if (!countedAsSelected)
            {
                SelectedHouseCount++;
                countedAsSelected = true;
            }
            IsStable = false;
            stableTimer = 0f;
            supportingColliders.Clear();

            if (IsOnTray)
            {
                IsOnTray = false;
                transform.SetParent(null, true);
                sourceTray?.NotifyHouseTaken(this, traySlotIndex);
                sourceTray = null;
                traySlotIndex = -1;
            }

            game?.NotifyHouseStateChanged();
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            if (countedAsSelected)
            {
                SelectedHouseCount = Mathf.Max(0, SelectedHouseCount - 1);
                countedAsSelected = false;
            }
            StartCoroutine(EnablePhysicsAfterRelease());
        }

        private IEnumerator EnablePhysicsAfterRelease()
        {
            yield return new WaitForFixedUpdate();

            if (IsSelected)
                yield break;

            body.isKinematic = false;
            body.useGravity = true;
            hasBeenReleased = true;
            IsStable = false;
            stableTimer = 0f;
            supportingColliders.Clear();
            game?.RegisterHouse(this);
            game?.NotifyHouseStateChanged();
        }

        private void EnsureReferences()
        {
            if (body == null)
                body = GetComponent<Rigidbody>();
            if (houseCollider == null)
                houseCollider = GetComponent<Collider>();
            if (grabInteractable == null)
                grabInteractable = GetComponent<XRGrabInteractable>();
        }
    }
}
