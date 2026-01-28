using UnityEngine;
using TMPro;
public class MainMenu : MonoBehaviour
{
    public GameObject Main, Join, Change;

    public TMP_InputField input;


    public RelayManager manager;
    public void JoinMenu()
    {
        Main.SetActive(false);
        Join.SetActive(true);
    }
    public void LogMenu()
    {
        Main.SetActive(false);
        Change.SetActive(true);
    }
    public void Back()
    {
        Change.SetActive(false);
        Join.SetActive(false);
        Main.SetActive(true);
    }

    public void TryJoinGame()
    {
        if(input.text.Length < 6) return;
        manager.StartClientWithRelay(input.text);
    }
}
