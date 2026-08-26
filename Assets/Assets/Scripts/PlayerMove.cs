using UnityEngine;

public class PlayerMove : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotateSpeed = 10f;
    [SerializeField] private float cameraDistance = 6f;
    [SerializeField] private float cameraHeight = 2.5f;
    [SerializeField] private float cameraRotationSpeed = 180f;
    [SerializeField] private float cameraPitchMin = -20f;
    [SerializeField] private float cameraPitchMax = 80f;
    [SerializeField] private Vector2 cameraAngle = new Vector2(0f, 20f);

    public Camera mainCamera;

    private Rigidbody rb;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        Cursor.lockState = CursorLockMode.Locked;
    }

    private void Update()
    {
        MoveAndCamera();
    }

    private void MoveAndCamera()
    {
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        if (mainCamera != null)
        {
            cameraAngle.x += Input.GetAxis("Mouse X") * cameraRotationSpeed * Time.deltaTime;
            cameraAngle.y -= Input.GetAxis("Mouse Y") * cameraRotationSpeed * Time.deltaTime;
            cameraAngle.y = Mathf.Clamp(cameraAngle.y, cameraPitchMin, cameraPitchMax);

            Quaternion rotation = Quaternion.Euler(cameraAngle.y, cameraAngle.x, 0f);
            Vector3 cameraForward = rotation * Vector3.forward;
            Vector3 cameraRight = rotation * Vector3.right;

            Vector3 movement = (cameraForward * moveZ + cameraRight * moveX).normalized;
            movement.y = 0f;

            if (movement.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(movement, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime);
            }

            rb.MovePosition(transform.position + movement * moveSpeed * Time.deltaTime);

            Vector3 desiredPosition = transform.position + rotation * new Vector3(0f, cameraHeight, -cameraDistance);
            mainCamera.transform.position = desiredPosition;
            mainCamera.transform.rotation = rotation;
        }
        else
        {
            Vector3 movement = new Vector3(moveX, 0f, moveZ);
            if (movement.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(movement, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotateSpeed * Time.deltaTime);
            }

            rb.MovePosition(transform.position + movement * moveSpeed * Time.deltaTime);
        }
    }
}
