using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class TextListAmalgamatorUtility : MonoBehaviour
{
    [Header("String Settings")]
    public List<TextList> textLists;
    public string prefix;
    public string delimiter;
    public string suffix;

    [Header("Display Settings")]
    public TextMeshPro textDisplay;
    public TextMeshProUGUI textDisplayPUGUI;
    public string textBuffer;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void GenerateText()
    {
        textBuffer = prefix;

        for(int i = 0; i < textLists.Count; i++)
        {
            textBuffer += textLists[i].GetRandomString();

            if(i < textLists.Count - 1)
                textBuffer += delimiter;
        }

        textBuffer += suffix;
    }

    public void UpdateText()
    {
        GenerateText();

        if(textDisplay != null)
            textDisplay.text = textBuffer;

        if(textDisplayPUGUI != null)
            textDisplayPUGUI.text = textBuffer;
    }
}
