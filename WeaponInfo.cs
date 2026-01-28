using System.Collections;
using TMPro;
using UnityEngine;

public class WeaponInfo : MonoBehaviour
{
    [SerializeField] private TMP_Text UiText;
    [SerializeField] float timer = 2f;

    void OnTriggerStay(Collider collision)
    {
        if(collision.gameObject.tag == "WeaponReader")
        {
            UiText.enabled = true;
            //StartCoroutine(TextReset());
        }
    }
    void OnTriggerExit(Collider other)
    {
        UiText.enabled = false;
    }

    private  IEnumerator TextReset()
    {
        yield return new WaitForSeconds(timer);
        UiText.enabled = false;
    }
}
