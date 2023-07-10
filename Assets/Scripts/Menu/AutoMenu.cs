using Microsoft.MixedReality.Toolkit.SpatialManipulation;
using Microsoft.MixedReality.Toolkit.UX;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;

public class AutoMenu : SubMenu
{
    private int[] SLIDER_MAXIMUM = { 127, 40 , 255};
    Config config;
    public Material withThreshold;
    public Material origin;
    Slider[] sliders;
    Task taskPruning;
    CancellationTokenSource source;
    CancellationToken token;
    PressableButton[] Buttons;
    // Start is called before the first frame update
    void Start()
    {
        config = GameObject.FindGameObjectWithTag("Config").GetComponent<Config>();
        sliders = GetComponentsInChildren<Slider>();
        Buttons = GetComponentsInChildren<PressableButton>();
        
        sliders[1].Value = config._somaRadius*2 / (float)SLIDER_MAXIMUM[1];
        
        sliders[0].OnValueUpdated.AddListener((SliderEventData data) => UpdateBkgValue(data));
        sliders[1].OnValueUpdated.AddListener((SliderEventData data) => UpdateRadiusValue(data));
        Buttons[0].OnClicked.AddListener(() =>AdjustSlider(0,true));
        Buttons[1].OnClicked.AddListener(() =>AdjustSlider(0,false));
        Buttons[2].OnClicked.AddListener(() =>AdjustSlider(1,true));
        Buttons[3].OnClicked.AddListener(() =>AdjustSlider(1,false));
        Buttons[4].OnClicked.AddListener(() => OnStartClicked());
        Buttons[5].OnClicked.AddListener(() => OnCancelClicked());
        Buttons[6].OnClicked.AddListener(() => OnModifyClicked());

        config.VRShaderType = Config.ShaderType.FlexibleThreshold;

        source = new CancellationTokenSource();
        token = source.Token;

        sliders[0].Value = config.BkgThresh / (float)SLIDER_MAXIMUM[0];
    }

    private void OnModifyClicked()
    {
        var iconSelector = Buttons[6].GetComponentInChildren<FontIconSelector>();
        if (Buttons[6].IsToggled)
        {
            config.gazeController.currentState = GazeController.EyeInteractionState.EditThresh;
            config.paintingBoard.GetComponent<ObjectManipulator>().enabled = true;
            iconSelector.CurrentIconName = "Icon 9";
        }
        else
        {
            config.gazeController.currentState = GazeController.EyeInteractionState.None;
            config.paintingBoard.GetComponent<ObjectManipulator>().enabled = false;
            iconSelector.CurrentIconName = "Icon 10";
        }
    }

    private void UpdateOffsetValue(SliderEventData data)
    {
        if (data.OldValue == data.NewValue) { return; }
        int newThresh = Mathf.RoundToInt(data.NewValue * SLIDER_MAXIMUM[2]);
        int fixedThresh = Mathf.RoundToInt(sliders[0].Value * SLIDER_MAXIMUM[0]);
        config.thresholdOffset = newThresh - fixedThresh;
        Debug.Log(newThresh); 
        TextMeshProUGUI[] textMeshProUGUIs = GetComponentsInChildren<TextMeshProUGUI>();
        var Text = textMeshProUGUIs[6];
        Text.text = $"Customized\n Thresh:\n {newThresh}";
    }

    void UpdateBkgValue(SliderEventData data)
    {
        if(data.OldValue==data.NewValue) { return; }
        int value = Mathf.RoundToInt(data.NewValue * SLIDER_MAXIMUM[0]);
        value = Mathf.Clamp(value, 3, SLIDER_MAXIMUM[0]);
        TextMeshProUGUI[] textMeshProUGUIs = GetComponentsInChildren<TextMeshProUGUI>();
        var bkgText = textMeshProUGUIs[0];
        bkgText.text = $"Background\n Threshold:\n {value}";
        config.BkgThresh = value;

        var threshold = config.tracer.InitThreshold();
        var connection = config.tracer.ConnectedPart(false);

        config.VRShaderType = Config.ShaderType.FlexibleThreshold;
        config._postProcessVolume.profile.GetSetting<BaseVolumeRendering>().connection.overrideState = true;
        config._postProcessVolume.profile.GetSetting<BaseVolumeRendering>().threshold.overrideState = true;
        config._postProcessVolume.profile.GetSetting<BaseVolumeRendering>().connection.value = connection;
        config._postProcessVolume.profile.GetSetting<BaseVolumeRendering>().threshold.value = threshold;

        config.tracer.TraceTrunk(0);
    }

    async void UpdateRadiusValue(SliderEventData data)
    {
        if (Mathf.Abs(data.OldValue-data.NewValue)* SLIDER_MAXIMUM[1] < 0.1) { return; }
        source.Cancel();
        int value = Mathf.RoundToInt(data.NewValue * SLIDER_MAXIMUM[1]);
        value = Mathf.Clamp(value, 10, SLIDER_MAXIMUM[1]);
        TextMeshProUGUI[] textMeshProUGUIs = GetComponentsInChildren<TextMeshProUGUI>();
        var radiusText = textMeshProUGUIs[3];
        radiusText.text = $"Soma Radius:\n {value}";
        config._somaRadius = value/2;

        source = new CancellationTokenSource();
        token = source.Token;
        float time = Time.realtimeSinceStartup;
        await Task.Run(() =>
        {
            if (token.IsCancellationRequested) { return; }
            config.tracer.Pruning(token);
        }, token);
        Debug.Log("Time:"+(Time.realtimeSinceStartup-time));
        config.tracer.CreateTree();
    }

    void OnStartClicked()
    {
        mainMenu.OnAutoFinished();
        Hide();
    }

    void OnCancelClicked()
    {
        config.tracer.ClearResult();
        Hide();
    }

    void AdjustSlider(int index, bool up)
    {
        sliders[index].Value += (up ? 1.0f : -1.0f) / SLIDER_MAXIMUM[index];
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public override void Hide()
    {
        //config._cube.GetComponent<MeshRenderer>().material = origin;
        config.VRShaderType = Config.ShaderType.Base;
        config.gazeController.currentState = GazeController.EyeInteractionState.None;
        base.Hide();
    }

    public override void Show()
    {
        //config._cube.GetComponent<MeshRenderer>().material = withThreshold;
        config.VRShaderType = Config.ShaderType.FlexibleThreshold;
        config.invoker.Clear();
        base.Show();
    }
}
