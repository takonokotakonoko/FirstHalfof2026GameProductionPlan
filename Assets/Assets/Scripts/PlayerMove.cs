using UnityEngine;

public class PlayerMove : MonoBehaviour
{
    [Header("�v���C���[�̈ړ����x")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotateSpeed = 10f;

    [Header("��l�̎��_�J����")]
    [SerializeField] private float cameraHeight = 1.6f;
    [SerializeField] private float cameraPitchMin = -80f;
    [SerializeField] private float cameraPitchMax = 80f;

    [Header("�J�����̊��x")]
    [SerializeField] private Vector2 cameraSensitivity = new Vector2(2f, 2f);
    [SerializeField] private Vector2 cameraAngle = Vector2.zero;

    [Header("��l�̃J����")]
    public Camera mainCamera;

    private Rigidbody rb;
    private Animator animator;

    public bool CanIncreaseMoveSpeed => CanIncreaseMoveSpeedUpTo(4f);

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();

        Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        if (Time.timeScale == 0f)
            return;

        MoveAndCamera();
    }

    public bool CanIncreaseMoveSpeedUpTo(float maximumSpeed)
    {
        return moveSpeed < maximumSpeed;
    }

    public void IncreaseMoveSpeed(float percentage, float maximumSpeed)
    {
        IncreaseMoveSpeed(percentage, 0.1f, maximumSpeed);
    }

    public void IncreaseMoveSpeed(float percentage, float minimumSpeed, float maximumSpeed)
    {
        if (percentage <= 0f || minimumSpeed <= 0f || maximumSpeed < minimumSpeed)
        {
            Debug.LogWarning("PlayerMove: 増加率と速度の最小値・最大値を確認してください。", this);
            return;
        }

        moveSpeed = Mathf.Clamp(moveSpeed * (1f + percentage), minimumSpeed, maximumSpeed);
    }

    private void MoveAndCamera()
    {
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        if (mainCamera != null)
        {
            cameraAngle.x += Input.GetAxis("Mouse X") * cameraSensitivity.x;
            cameraAngle.y -= Input.GetAxis("Mouse Y") * cameraSensitivity.y;
            cameraAngle.y = Mathf.Clamp(cameraAngle.y, cameraPitchMin, cameraPitchMax);

            Quaternion rotation = Quaternion.Euler(cameraAngle.y, cameraAngle.x, 0f);
            Quaternion yawRotation = Quaternion.Euler(0f, cameraAngle.x, 0f);
            Vector3 cameraForward = yawRotation * Vector3.forward;
            Vector3 cameraRight = yawRotation * Vector3.right;

            Vector3 movement = (cameraForward * moveZ + cameraRight * moveX).normalized;

            // ?��A?��j?��?��?��[?��V?��?��?��?��?��p
            if (animator != null)
            {
                animator.SetFloat("Speed", movement.magnitude);
            }

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                yawRotation,
                rotateSpeed * Time.deltaTime
            );

            rb.MovePosition(
                transform.position + movement * moveSpeed * Time.deltaTime
            );

            mainCamera.transform.position = transform.position + Vector3.up * cameraHeight;
            mainCamera.transform.rotation = rotation;
        }
        else
        {
            Vector3 movement = new Vector3(moveX, 0f, moveZ);

            // ?��A?��j?��?��?��[?��V?��?��?��?��?��p
            if (animator != null)
            {
                animator.SetFloat("Speed", movement.magnitude);
            }

            if (movement.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation =
                    Quaternion.LookRotation(movement, Vector3.up);

                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    targetRotation,
                    rotateSpeed * Time.deltaTime
                );
            }

            rb.MovePosition(
                transform.position + movement * moveSpeed * Time.deltaTime
            );
        }
    }


    public void SetCameraSensitivity(float sensitivity)
    {
        cameraSensitivity = new Vector2(sensitivity, sensitivity);
    }
}