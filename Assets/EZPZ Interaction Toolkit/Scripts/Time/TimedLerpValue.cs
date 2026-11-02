//EZPZ Interaction Toolkit
//by Matt Cabanag
//created 17 Aug 2026

using UnityEngine;
using UnityEngine.Events;

public class TimedLerpValue : MonoBehaviour
{
    public UnityEvent onClockZero;
    public float endTime = 5;
    public float clock = 0;
    public float lerpValue;
    public bool clockRunning = true;

    // Update is called once per frame
    void Update()
    {
        if(clockRunning)
        {
            if(clock < endTime)
            {
                clock += Time.deltaTime;
                lerpValue = 1 - ((endTime - clock) / endTime);
            }
            else
            {
                clock = endTime;
                lerpValue = 1;
                clockRunning = false;
                onClockZero.Invoke();
            }
        }

    }

    public void Reset()
    {
        clock = 0;
        lerpValue = 0;
        clockRunning = true;
    }

    public void PauseClock()
    {
        clockRunning = false;
    }

    public void ResumeClock()
    {
        ResumeClock();
    }

    public void StartClock()
    {
        clockRunning = true;
    }

}
