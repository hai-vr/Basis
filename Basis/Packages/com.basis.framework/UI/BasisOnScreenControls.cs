using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management.Devices.Desktop;
using System;
using UnityEngine;
using UnityEngine.InputSystem.OnScreen;

public class BasisOnScreenControls : MonoBehaviour
{
    public BasisScreenUIJoyStick LeftControl;
    public BasisScreenUIJoyStick RightControl;
    public void OnEnable()
    {
        LeftControl.OnStickDown += OnStickDownLeft;
        LeftControl.OnStickMove += OnStickMoveLeft;

        RightControl.OnStickDown += OnStickDownRight;
        RightControl.OnStickMove += OnStickMoveRight;
    }
    public void OnDisable()
    {
        LeftControl.OnStickDown -= OnStickDownLeft;
        LeftControl.OnStickMove -= OnStickMoveLeft;

        RightControl.OnStickDown -= OnStickDownRight;
        RightControl.OnStickMove -= OnStickMoveRight;
    }

    private void OnStickMoveLeft(Vector2 vector)
    {
        BasisLocalPlayer.Instance.LocalCharacterDriver.SetMovementVector(vector);
        BasisLocalPlayer.Instance.LocalCharacterDriver.UpdateMovementSpeed(BasisLocalInputActions.Instance.IsRunHeld);
    }

    private void OnStickDownLeft(Vector2 vector)
    {

    }
    private void OnStickMoveRight(Vector2 vector)
    {
        BasisLocalInputActions.Instance.OnLookAction(vector, 3);
    }

    private void OnStickDownRight(Vector2 vector)
    {

    }
}
