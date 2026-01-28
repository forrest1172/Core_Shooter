using System;
using TMPro;
using UnityEngine;

public class PlayerPauseMenu : MonoBehaviour
{
    public Canvas pauseMenu;
    public TMP_InputField joinCodeText;
    
    public bool isPaused = false;
    void Update()
    {
        //cursor lock state handled in playerMotor
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            GetRoomCode();
            if (!isPaused)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                isPaused = true;
                pauseMenu.enabled = true;
            }
            else
            {   
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                isPaused = false;
                pauseMenu.enabled = false;
            }

        }
        
    }

    public void GetRoomCode()
    {
        String joinText = RelayManager.Instance.currentJoinCode;
        //display room code
        joinCodeText.text = joinText;
    }
}


