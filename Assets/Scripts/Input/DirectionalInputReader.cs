using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Cardinal directions used across the Simon-says cooking loop.
/// </summary>
public enum Direction
{
    Up,
    Down,
    Left,
    Right
}

/// <summary>
/// Wraps the Input System and exposes discrete directional press events.
/// One instance is created per player so the game generalizes to N players.
/// The reader derives a nearest-cardinal direction from the template
/// <c>Player/Move</c> Vector2 action rather than adding a parallel pipeline.
/// </summary>
public class DirectionalInputReader
{
    private const float DeadzoneSquared = 0.25f;

    private InputAction _moveAction;
    private bool _armed = true;

    /// <summary>Raised once per discrete directional press.</summary>
    public event Action<Direction> OnDirectionPressed;

    /// <summary>Creates a reader bound to the supplied Move action.</summary>
    public DirectionalInputReader(InputAction moveAction)
    {
        SetMoveAction(moveAction);
    }

    /// <summary>Rebinds the underlying Move action.</summary>
    public void SetMoveAction(InputAction moveAction)
    {
        if (_moveAction != null)
        {
            Unsubscribe();
        }

        _moveAction = moveAction;
    }

    /// <summary>Enables input and begins raising press events.</summary>
    public void Enable()
    {
        if (_moveAction == null)
        {
            return;
        }

        Subscribe();
        _moveAction.Enable();
    }

    /// <summary>Disables input and stops raising press events.</summary>
    public void Disable()
    {
        if (_moveAction == null)
        {
            return;
        }

        Unsubscribe();
        _moveAction.Disable();
    }

    private void Subscribe()
    {
        _moveAction.performed += HandlePerformed;
        _moveAction.canceled += HandleCanceled;
    }

    private void Unsubscribe()
    {
        _moveAction.performed -= HandlePerformed;
        _moveAction.canceled -= HandleCanceled;
    }

    private void HandleCanceled(InputAction.CallbackContext context)
    {
        // Re-arm once the stick/keys return to neutral so holds don't repeat.
        _armed = true;
    }

    private void HandlePerformed(InputAction.CallbackContext context)
    {
        Vector2 value = context.ReadValue<Vector2>();
        if (value.sqrMagnitude < DeadzoneSquared)
        {
            return;
        }

        if (!_armed)
        {
            return;
        }

        _armed = false;
        OnDirectionPressed?.Invoke(ToCardinal(value));
    }

    private static Direction ToCardinal(Vector2 value)
    {
        if (Mathf.Abs(value.x) >= Mathf.Abs(value.y))
        {
            return value.x >= 0f ? Direction.Right : Direction.Left;
        }

        return value.y >= 0f ? Direction.Up : Direction.Down;
    }
}
