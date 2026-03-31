using System;
using System.Diagnostics.CodeAnalysis;
using SaintsField;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraControl : Singleton<CameraControl>
{
    [NotNull] CameraInput? CameraActions = default;
    [NotNull] InputAction? Movement = default;
    [NotNull] InputAction? KeyZoom = default;
    [NotNull] Transform? CameraTransform = default;

    [Header("Movement")]

    [SerializeField] float MaxSpeed = 20f;
    [SerializeField] float Acceleration = 10f;
    [SerializeField] float Damping = 10f;

    [Header("Zoom")]

    [SerializeField] float KeyZoomSpeed = 0.06f;
    [SerializeField] float WheelZoomSpeed = 80f;
    [SerializeField] float ZoomDampening = 7.5f;
    [SerializeField] float MinHeight = 1f;
    [SerializeField] float MaxHeight = 500f;

    [Header("Rotation")]

    [SerializeField] float MaxRotationSpeed = 0.2f;

    [Header("Edge Movement")]

    [Range(0f, 0.1f)]
    [SerializeField] float EdgeTolerance = 0.05f;
    [SerializeField] bool UseScreenEdge = true;

    public bool IsDragging
    {
        get
        {
            if ((!Mouse.current.middleButton.isPressed || !Keyboard.current.shiftKey.isPressed) &&
                !Mouse.current.middleButton.wasReleasedThisFrame)
            { return false; }
            if (UI.IsUIFocused) return false;
            return (
                Mouse.current.position.ReadValue() - startDragScreen
            ).SqrMagnitude() > 5f;
        }
    }

    public bool IsZooming
    {
        get
        {
            if ((!Mouse.current.middleButton.isPressed || !Keyboard.current.ctrlKey.isPressed) &&
                !Mouse.current.middleButton.wasReleasedThisFrame)
            { return false; }
            if (UI.IsUIFocused) return false;
            return (
                Mouse.current.position.ReadValue() - startDragZoomScreen
            ).SqrMagnitude() > 5f;
        }
    }

    [Header("Debug")]
    [SerializeField, ReadOnly] float Speed;
    [SerializeField, ReadOnly] Vector3 velocity;
    [SerializeField, ReadOnly] float ZoomHeight;
    [SerializeField, ReadOnly] Vector3 horizontalVelocity;
    [SerializeField, ReadOnly] Vector3 lastPosition;
    [SerializeField, ReadOnly] Vector3 startDragWorld;
    [SerializeField, ReadOnly] Vector2 startDragScreen;
    [SerializeField, ReadOnly] Vector2 startDragZoomScreen;

    protected override void Awake()
    {
        base.Awake();
        CameraActions = new CameraInput();
        CameraTransform = GetComponentInChildren<Camera>().transform;
    }

    void OnEnable()
    {
        ZoomHeight = CameraTransform.localPosition.y;

        lastPosition = transform.position;
        Movement = CameraActions.Camera.Movement;
        KeyZoom = CameraActions.Camera.KeyZoom;

        CameraActions.Camera.Rotate.performed += RotateCamera;
        CameraActions.Camera.ScrollZoom.performed += ZoomWithWheel;

        CameraActions.Camera.Enable();
    }

    void OnDisable()
    {
        CameraActions.Camera.Rotate.performed -= RotateCamera;
        CameraActions.Camera.ScrollZoom.performed -= ZoomWithWheel;

        CameraActions.Camera.Disable();
    }

    void Update()
    {
        //if (UIManager.Instance.AnyUIVisible)
        //{
        //    lastPosition = transform.position;
        //    startDragWorld = default;
        //    startDragScreen = default;
        //    return;
        //}

        MoveWithKeys();
        ZoomWithKeys();
        ZoomWithMouse();
        if (UseScreenEdge)
        { MoveWithScreenEdge(); }
        MoveWithDrag();

        UpdateVelocity();
        ApplyZoom();
        ApplyPosition();

        if (TerrainGenerator.Instance.TrySample(new float2(transform.position.x, transform.position.z), out float height))
        {
            transform.position = new Vector3(transform.position.x, Mathf.Lerp(transform.position.y, height, 5f * Time.deltaTime), transform.position.z);
        }

        if (TerrainGenerator.Instance.TrySample(new float2(CameraTransform.position.x, CameraTransform.position.z), out height))
        {
            if (CameraTransform.position.y <= height + MinHeight)
            {
                CameraTransform.position = new Vector3(CameraTransform.position.x, height + MinHeight, CameraTransform.position.z);
            }
        }

        if (ConnectionManager.ClientWorld != null && Time.timeSinceLevelLoad > 5f)
        {
            PlayerPositionSystemClient.GetInstance(ConnectionManager.ClientWorld.Unmanaged).CurrentPosition = CameraTransform.position;
        }
    }

    void UpdateVelocity()
    {
        if (Time.deltaTime > 0)
        {
            horizontalVelocity = (transform.position - lastPosition) / Time.deltaTime;
            horizontalVelocity.y = 0;
        }
        lastPosition = transform.position;
    }

    Vector3 GetCameraRight()
    {
        Vector3 right = CameraTransform.right;
        right.y = 0;
        return right;
    }

    Vector3 GetCameraForward()
    {
        Vector3 forward = CameraTransform.forward;
        forward.y = 0;
        return forward;
    }


    void RotateCamera(InputAction.CallbackContext inputValue)
    {
        if (UI.IsUIFocused) return;

        if (!Mouse.current.middleButton.isPressed || Keyboard.current.shiftKey.isPressed || Keyboard.current.ctrlKey.isPressed) return;

        float x = inputValue.ReadValue<Vector2>().x;
        float y = inputValue.ReadValue<Vector2>().y;
        transform.rotation = Quaternion.Euler(
            math.clamp(y * MaxRotationSpeed + transform.rotation.eulerAngles.x, 5f, 85f),
            x * MaxRotationSpeed + transform.rotation.eulerAngles.y,
            0f
        );
    }


    void ZoomWithKeys()
    {
        if (UI.IsUIFocused) return;

        float value = -KeyZoom.ReadValue<Vector2>().y;
        if (Math.Abs(value) <= 0f) return;

        ZoomHeight *= Mathf.Pow(2, value * KeyZoomSpeed);
        ZoomHeight = math.clamp(ZoomHeight, MinHeight, MaxHeight);
    }

    void ZoomWithWheel(InputAction.CallbackContext inputValue)
    {
        if (UI.IsUIFocused) return;

        float value = -inputValue.ReadValue<Vector2>().y;
        if (Math.Abs(value) <= 0f) return;

        ZoomHeight *= Mathf.Pow(2, value * WheelZoomSpeed);
        ZoomHeight = math.clamp(ZoomHeight, MinHeight, MaxHeight);
    }

    void ZoomWithMouse()
    {
        if (UI.IsUIFocused) return;

        if (!Mouse.current.middleButton.isPressed || !Keyboard.current.ctrlKey.isPressed)
        {
            startDragZoomScreen = default;
            return;
        }

        Vector2 mousePosition = InputUtils.Mouse.ViewportPosition;

        if (startDragZoomScreen == default)
        {
            startDragZoomScreen = mousePosition;
            return;
        }

        float beginDistance = startDragZoomScreen.y;
        float currentDistance = mousePosition.y;
        float delta = beginDistance - currentDistance;

        startDragZoomScreen = mousePosition;

        ZoomHeight *= Mathf.Pow(2, delta * 5f);
        ZoomHeight = math.clamp(ZoomHeight, MinHeight, MaxHeight);
    }

    void ApplyZoom()
    {
        Vector3 zoomTarget = new(
            0f,
            ZoomHeight,
            0f
        );

        CameraTransform.localPosition = Vector3.Lerp(CameraTransform.localPosition, zoomTarget, Time.deltaTime * ZoomDampening);
    }


    void MoveWithKeys()
    {
        if (UI.IsUIFocused) return;
        if (startDragWorld != default) return;

        Vector3 inputValue =
            Movement.ReadValue<Vector2>().x * GetCameraRight() +
            Movement.ReadValue<Vector2>().y * GetCameraForward();
        inputValue.Normalize();

        if (inputValue.sqrMagnitude <= 0.1f) return;

        if (Keyboard.current.shiftKey.isPressed)
        { inputValue *= 2f; }

        velocity += inputValue * (ZoomHeight * 0.1f);
    }

    void MoveWithScreenEdge()
    {
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Vector3 moveDirection = Vector3.zero;

        if (mousePosition.x < EdgeTolerance * Screen.width)
        { moveDirection -= GetCameraRight(); }
        else if (mousePosition.x > (1f - EdgeTolerance) * Screen.width)
        { moveDirection += GetCameraRight(); }

        if (mousePosition.y < EdgeTolerance * Screen.height)
        { moveDirection -= GetCameraForward(); }
        else if (mousePosition.y > (1f - EdgeTolerance) * Screen.height)
        { moveDirection += GetCameraForward(); }

        moveDirection.Normalize();

        velocity += moveDirection * (ZoomHeight * 0.1f);
    }

    void MoveWithDrag()
    {
        if (!Mouse.current.middleButton.isPressed || !Keyboard.current.shiftKey.isPressed || Movement.ReadValue<Vector2>() != default)
        {
            startDragWorld = default;
            startDragScreen = default;
            return;
        }

        if (UI.IsUIFocused)
        {
            return;
        }

        Plane plane = new(Vector3.up, Vector3.zero);
        UnityEngine.Ray ray = MainCamera.Camera.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (plane.Raycast(ray, out float distance) && distance < 300f)
        {
            if (startDragWorld == default)
            {
                startDragWorld = ray.GetPoint(distance);
                startDragScreen = Mouse.current.position.ReadValue();
            }
            else
            {
                velocity += startDragWorld - ray.GetPoint(distance);
            }
        }
    }

    void ApplyPosition()
    {
        if (velocity.sqrMagnitude > 0.001f)
        {
            Speed = Mathf.Lerp(Speed, MaxSpeed, Time.deltaTime * Acceleration);
            transform.position += velocity * (Speed * Time.deltaTime);
        }
        else
        {
            Speed = Mathf.Lerp(Speed, 0f, Time.deltaTime * Damping);
            horizontalVelocity = Vector3.Lerp(horizontalVelocity, Vector3.zero, Time.deltaTime * Damping);
            transform.position += horizontalVelocity * Time.deltaTime;
        }

        velocity = Vector3.zero;
    }
}
