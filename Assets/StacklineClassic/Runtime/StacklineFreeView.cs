using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Wukong.StacklineClassic
{
    /// <summary>
    /// Lightweight desktop and XR free-view controls for the Stackline camera.
    /// It deliberately moves only the camera, never the game root or arena anchor.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StacklineFreeView : MonoBehaviour
    {
        [Header("Free view")]
        [SerializeField, Min(0.01f)] private float lookSensitivity = 0.12f;
        [SerializeField, Min(1f)] private float stickLookSpeed = 100f;
        [SerializeField, Range(0f, 0.95f)] private float stickDeadZone = 0.15f;
        [SerializeField, Min(0.1f)] private float moveSpeed = 4.5f;
        [SerializeField, Min(1f)] private float sprintMultiplier = 2.2f;
        [SerializeField, Min(0.01f)] private float zoomStep = 0.006f;
        [SerializeField, Min(0.1f)] private float minimumOrthographicSize = 2.4f;
        [SerializeField, Min(0.1f)] private float maximumOrthographicSize = 22f;

        private Camera viewCamera;
        private float yaw;
        private float pitch;
        private bool looking;

        /// <summary>
        /// Adds the controller once, preserving the camera's current world pose.
        /// </summary>
        public static StacklineFreeView EnsureAttached(Camera camera)
        {
            if (camera == null)
                return null;

            StacklineFreeView freeView = camera.GetComponent<StacklineFreeView>();
            return freeView != null ? freeView : camera.gameObject.AddComponent<StacklineFreeView>();
        }

        private void Awake()
        {
            viewCamera = GetComponent<Camera>();
            CacheAngles();
        }

        private void OnEnable()
        {
            CacheAngles();
        }

        private void Update()
        {
            if (viewCamera == null)
                viewCamera = GetComponent<Camera>();

            HandleMouseLook();
            HandleStickLook();
            HandleMovement();
            HandleZoom();
        }

        private void HandleMouseLook()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            if (mouse.rightButton.wasPressedThisFrame)
                looking = !PointerIsOverUi();
            if (!mouse.rightButton.isPressed)
            {
                looking = false;
                return;
            }
            if (!looking)
                return;

            Vector2 delta = mouse.delta.ReadValue();
            if (delta.sqrMagnitude <= Mathf.Epsilon)
                return;

            yaw += delta.x * lookSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * lookSensitivity, -85f, 85f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void HandleStickLook()
        {
            Vector2 look = ReadXrStick(UnityEngine.XR.XRNode.RightHand);
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
                look = DominantInput(look, ApplyDeadZone(gamepad.rightStick.ReadValue()));

            if (look.sqrMagnitude <= Mathf.Epsilon)
                return;

            float lookStep = stickLookSpeed * Time.unscaledDeltaTime;
            yaw += look.x * lookStep;
            pitch = Mathf.Clamp(pitch - look.y * lookStep, -85f, 85f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void HandleMovement()
        {
            Keyboard keyboard = Keyboard.current;
            float horizontal = 0f;
            float forward = 0f;
            float vertical = 0f;
            bool sprinting = false;

            if (keyboard != null)
            {
                horizontal += (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
                forward += (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
                vertical += (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f);
                sprinting = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            }

            Vector2 planar = ReadXrStick(UnityEngine.XR.XRNode.LeftHand);
            vertical += ReadXrVertical();

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                planar = DominantInput(planar, ApplyDeadZone(gamepad.leftStick.ReadValue()));
                vertical += (gamepad.rightShoulder.isPressed ? 1f : 0f)
                    - (gamepad.leftShoulder.isPressed ? 1f : 0f);
            }

            horizontal += planar.x;
            forward += planar.y;
            vertical = Mathf.Clamp(vertical, -1f, 1f);

            Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Vector3.forward;
            flatForward.Normalize();
            Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward);

            Vector3 movement = flatRight * horizontal + flatForward * forward + Vector3.up * vertical;
            if (movement.sqrMagnitude <= 0.0001f)
                return;

            float speed = moveSpeed;
            if (sprinting)
                speed *= sprintMultiplier;
            transform.position += Vector3.ClampMagnitude(movement, 1f) * speed * Time.unscaledDeltaTime;
        }

        private void HandleZoom()
        {
            Mouse mouse = Mouse.current;
            if (viewCamera == null || mouse == null)
                return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) <= Mathf.Epsilon)
                return;

            if (viewCamera.orthographic)
            {
                viewCamera.orthographicSize = Mathf.Clamp(viewCamera.orthographicSize - scroll * zoomStep,
                    minimumOrthographicSize, maximumOrthographicSize);
            }
            else
            {
                transform.position += transform.forward * (scroll * zoomStep * 4f);
            }
        }

        private Vector2 ReadXrStick(UnityEngine.XR.XRNode node)
        {
            UnityEngine.XR.InputDevice device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid ||
                !device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 value))
                return Vector2.zero;

            return ApplyDeadZone(value);
        }

        private static float ReadXrVertical()
        {
            bool leftGrip = ReadXrButton(UnityEngine.XR.XRNode.LeftHand, UnityEngine.XR.CommonUsages.gripButton);
            bool rightGrip = ReadXrButton(UnityEngine.XR.XRNode.RightHand, UnityEngine.XR.CommonUsages.gripButton);
            return (rightGrip ? 1f : 0f) - (leftGrip ? 1f : 0f);
        }

        private static bool ReadXrButton(UnityEngine.XR.XRNode node, UnityEngine.XR.InputFeatureUsage<bool> usage)
        {
            UnityEngine.XR.InputDevice device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(usage, out bool pressed) && pressed;
        }

        private Vector2 ApplyDeadZone(Vector2 value)
        {
            float magnitude = value.magnitude;
            if (magnitude <= stickDeadZone)
                return Vector2.zero;

            float scaledMagnitude = Mathf.InverseLerp(stickDeadZone, 1f, Mathf.Min(magnitude, 1f));
            return value.normalized * scaledMagnitude;
        }

        private static Vector2 DominantInput(Vector2 first, Vector2 second)
        {
            return second.sqrMagnitude > first.sqrMagnitude ? second : first;
        }

        private void CacheAngles()
        {
            Vector3 angles = transform.eulerAngles;
            yaw = angles.y;
            pitch = NormalizeAngle(angles.x);
        }

        private static float NormalizeAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }

        private static bool PointerIsOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}
