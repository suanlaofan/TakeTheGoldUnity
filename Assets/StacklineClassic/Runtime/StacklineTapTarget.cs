using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Wukong.StacklineClassic
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class StacklineTapTarget : MonoBehaviour
    {
        private Collider targetCollider;
        private XRSimpleInteractable xrInteractable;

        public event Action Pressed;

        private void Awake()
        {
            targetCollider = GetComponent<Collider>();
            xrInteractable = GetComponent<XRSimpleInteractable>();
            if (xrInteractable != null)
                xrInteractable.selectEntered.AddListener(OnSelected);
        }

        private void OnDestroy()
        {
            if (xrInteractable != null)
                xrInteractable.selectEntered.RemoveListener(OnSelected);
        }

        public void SetInteractable(bool enabled)
        {
            if (targetCollider != null)
                targetCollider.enabled = enabled;
            if (xrInteractable != null)
                xrInteractable.enabled = enabled;
        }

        private void OnSelected(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs _)
        {
            Pressed?.Invoke();
        }

        private void OnMouseDown()
        {
            Pressed?.Invoke();
        }
    }
}
