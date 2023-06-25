using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ShowFPS : MonoBehaviour
{
    public float showTime = 0.2f;
    public Text FpsInfo;
    public Text AverageFpsInfo;

    private int count = 0;
    private int totalcount = 0;
    private float deltaTime = 0f;
    private float totalTime = 0f;

    // Update is called once per frame
    public void Reset()
    {
        count = 0;
        totalTime = 0;
        totalcount = 0;
        deltaTime = 0;
    }
    void Update()
    {
        count++;
        totalcount++;
        deltaTime += Time.deltaTime;
        totalTime += Time.deltaTime;
        if (deltaTime >= showTime)
        {
            float fps = count / deltaTime;
            float averageFps = totalcount / totalTime;
            float milliSecond = deltaTime * 1000 / count;
            string strFpsInfo = string.Format(" FPS:{0:0.} {1:0.0} ms", fps, milliSecond);
            string strAFpsInfo = string.Format(" Average FPS:{0:0.}", averageFps);
            FpsInfo.text = strFpsInfo;
            AverageFpsInfo.text = strAFpsInfo;
            count = 0;
            deltaTime = 0f;
        }
    }
}
