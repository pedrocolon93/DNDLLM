using UnityEngine;

namespace DnD.Character
{
    /// <summary>
    /// Simple 2D movement controller for player character
    /// Grid-based movement for tactical gameplay
    /// </summary>
    [RequireComponent(typeof(CharacterStats))]
    public class PlayerController2D : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private bool useGridMovement = true;
        [SerializeField] private float gridSize = 1f;

        [Header("Components")]
        private CharacterStats stats;
        private SpriteRenderer spriteRenderer;
        private Vector2 targetPosition;
        private bool isMoving = false;

        private void Awake()
        {
            stats = GetComponent<CharacterStats>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            targetPosition = transform.position;
        }

        private void Update()
        {
            HandleMovementInput();
            UpdateMovement();
        }

        private void HandleMovementInput()
        {
            if (isMoving)
                return;

            Vector2 input = Vector2.zero;

            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
                input = Vector2.up;
            else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
                input = Vector2.down;
            else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
                input = Vector2.left;
            else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
                input = Vector2.right;

            if (input != Vector2.zero)
            {
                if (useGridMovement)
                {
                    targetPosition = (Vector2)transform.position + input * gridSize;
                    isMoving = true;
                }
                else
                {
                    transform.position += (Vector3)input * moveSpeed * Time.deltaTime;
                }
            }
        }

        private void UpdateMovement()
        {
            if (!isMoving)
                return;

            float step = moveSpeed * Time.deltaTime;
            transform.position = Vector2.MoveTowards(transform.position, targetPosition, step);

            if (Vector2.Distance(transform.position, targetPosition) < 0.01f)
            {
                transform.position = targetPosition;
                isMoving = false;
            }
        }

        public void MoveTo(Vector2 position)
        {
            if (useGridMovement)
            {
                // Snap to grid
                targetPosition = new Vector2(
                    Mathf.Round(position.x / gridSize) * gridSize,
                    Mathf.Round(position.y / gridSize) * gridSize
                );
                isMoving = true;
            }
            else
            {
                transform.position = position;
            }
        }

        public void MoveInDirection(Vector2 direction, float distance = 1f)
        {
            targetPosition = (Vector2)transform.position + direction.normalized * distance;
            isMoving = true;
        }

        private void OnDrawGizmos()
        {
            if (useGridMovement)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(transform.position, Vector3.one * gridSize);
            }
        }
    }
}
