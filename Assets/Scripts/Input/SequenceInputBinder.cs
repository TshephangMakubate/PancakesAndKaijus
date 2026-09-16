using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Builds one button action per player for the 2v2 sequence mechanic.
/// <para>
/// The actions are created in code rather than added to
/// <c>InputSystem_Actions.inputactions</c> on purpose: that asset is the
/// project-wide template, its WASD/arrow composite fuses both key sets into a
/// single binding, and separating players by <em>gamepad</em> through one shared
/// asset would need <c>PlayerInputManager</c> or per-player asset clones.
/// Binding directly to a specific pad's control is both simpler and exact.
/// </para>
/// </summary>
public class SequenceInputBinder : IDisposable
{
    /// <summary>Players in a full 2v2 match.</summary>
    public const int PlayerCount = 4;

    private readonly PlayerButtonInput[] _players = new PlayerButtonInput[PlayerCount];

    /// <summary>Raised when a player presses, carrying the flat 0..3 player index.</summary>
    public event Action<int> OnPlayerPressed;

    /// <summary>Creates and binds one action per player from the authored config.</summary>
    public SequenceInputBinder(SequenceConfig config)
    {
        for (int i = 0; i < PlayerCount; i++)
        {
            var action = new InputAction($"SequencePress{i + 1}", InputActionType.Button);

            string key = config != null ? config.KeyFor(i) : string.Empty;
            if (!string.IsNullOrEmpty(key))
            {
                action.AddBinding(key);
            }

            if (config == null || config.UseGamepads)
            {
                BindGamepad(action, config, i);
            }

            var reader = new PlayerButtonInput(action, ownsAction: true);
            int playerIndex = i;
            reader.OnPressed += () => OnPlayerPressed?.Invoke(playerIndex);
            _players[i] = reader;
        }
    }

    /// <summary>The reader for one player, for HUD prompts or rebinding.</summary>
    public PlayerButtonInput Player(int index)
    {
        return index >= 0 && index < PlayerCount ? _players[index] : null;
    }

    /// <summary>Enables every player's button.</summary>
    public void Enable()
    {
        for (int i = 0; i < _players.Length; i++)
        {
            _players[i]?.Enable();
        }
    }

    /// <summary>Disables every player's button.</summary>
    public void Disable()
    {
        for (int i = 0; i < _players.Length; i++)
        {
            _players[i]?.Disable();
        }
    }

    /// <summary>Releases every action this binder created.</summary>
    public void Dispose()
    {
        for (int i = 0; i < _players.Length; i++)
        {
            _players[i]?.Dispose();
            _players[i] = null;
        }
    }

    /// <summary>
    /// Pairs a player to the nth connected gamepad, binding to that pad's own
    /// control so two pads never drive the same player.
    /// </summary>
    private static void BindGamepad(InputAction action, SequenceConfig config, int playerIndex)
    {
        if (playerIndex >= Gamepad.all.Count)
        {
            return;
        }

        Gamepad pad = Gamepad.all[playerIndex];
        if (pad == null)
        {
            return;
        }

        string path = config != null ? config.GamepadButton : null;
        InputControl control = null;

        if (!string.IsNullOrEmpty(path))
        {
            control = InputControlPath.TryFindControl(pad, path);
        }

        control ??= pad.buttonSouth;

        if (control != null)
        {
            action.AddBinding(control);
        }
        else
        {
            Debug.LogWarning($"Waffle Party: Could not bind player {playerIndex + 1} to gamepad '{pad.displayName}'.");
        }
    }
}
