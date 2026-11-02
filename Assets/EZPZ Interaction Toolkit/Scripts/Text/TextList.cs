using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class TextList : MonoBehaviour
{
    public List<string> textList = new List<string>();

    public string GetString(int index)
    {
        return textList[index];
    }

    public string GetRandomString()
    {
        int random = Random.Range(0, textList.Count);
        return textList[random];
    }
}
