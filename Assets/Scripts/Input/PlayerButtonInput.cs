using System;
using UnityEngine.InputSystem;

/// <summary>
/// Wraps the Input System and exposes a single discrete button press for one
/// player. Deliberately shaped like <see cref="DirectionalInputReader"/> — a
/// plain C# class around one <see cref="InputAction"/>, not a MonoBehaviour —
/// so the co-op mechanic reuses the project's existing input approach.
/// </summary>
public class PlayerButtonInput : IDisposable
{
    private readonly InputAction _action;
    private readonly bool _ownsAction;

    private bool _subscribed;

    /// <summary>Raised once per discrete press.</summary>
    public event Action OnPressed;

    /// <summary>
    /// Creates a reader around an action. When <paramref name="ownsAction"/> is
    /// true the action is disposed along with this reader.
    /// </summary>
    public PlayerButtonInput(InputAction action, bool ownsAction = false)
    {
        _action = action;
        _ownsAction = ownsAction;
    }

    /// <summary>The underlying action, for rebinding or display.</summary>
    public InputAction Action => _action;

    /// <summary>True while the button is held down, for hold-and-release mechanics.</summary>
    public bool IsPressed => _action != null && _action.enabled && _action.IsPressed();

    /// <summary>Enables input and begins raising press events.</summary>
    public void Enable()
    {
        if (_action == null)
        {
            return;
        }

        if (!_subscribed)
        {
            _action.performed += HandlePerformed;
            _subscribed = true;
        }

        _action.Enable();
    }

    /// <summary>Disables input and stops raising press events.</summary>
    public void Disable()
    {
        if (_action == null)
        {
            return;
        }

        if (_subscribed)
        {
            _action.performed -= HandlePerformed;
            _subscribed = false;
        }

        _action.Disable();
    }

    /// <summary>Releases the action when this reader created it.</summary>
    public void Dispose()
    {
        Disable();

        if (_ownsAction)
        {
            _action?.Dispose();
        }
    }

    private void HandlePerformed(InputAction.CallbackContext context)
    {
        OnPressed?.Invoke();
    }
}
