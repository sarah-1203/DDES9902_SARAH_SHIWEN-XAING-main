//EZPZ Interaction Toolkit
//by Matt Cabanag
//created 17 Aug 2026

using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(TimedLerpValue))]
public class TimedScale : MonoBehaviour
{
    public Vector3 startScale = new Vector3(0.1f, 0.1f, 0.1f);
    public Vector3 endScale = new Vector3(2, 2, 2);

    public bool resetOnDisable = true;

    public Vector3 originalScale;

    public TimedLerpValue myLerpValue;

    void Start()
    {
        originalScale = transform.localScale;

        if (myLerpValue == null)
            myLerpValue = GetComponent<TimedLerpValue>();

        transform.localScale = startScale;
    }

    public void OnDisable()
    {
        if (resetOnDisable)
        {
            transform.localScale = originalScale;
            myLerpValue.Reset();
        }
    }

    // Update is called once per frame
    void Update()
    {
        if(myLerpValue.clockRunning)
        {
            transform.localScale = Vector3.Lerp(startScale,endScale,myLerpValue.lerpValue);
        }
    }
}
