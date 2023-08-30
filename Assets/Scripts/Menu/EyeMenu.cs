using CommandStructure;
using Microsoft.MixedReality.Toolkit.UX;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEditor;
using UnityEngine;

public class EyeMenu : SubMenu
{
    Config config;
    Slider[] sliders;
    [SerializeField] int[] SLIDER_MAXIMUM = { 128 };

    // Start is called before the first frame update
    void Start()
    {
        config = GameObject.FindGameObjectWithTag("Config").GetComponent<Config>();
        sliders = GetComponentsInChildren<Slider>();
        var Buttons = GetComponentsInChildren<PressableButton>();
        Debug.Log(Buttons.Length);
        sliders[0].OnValueUpdated.AddListener((SliderEventData data) => UpdateViewValue(data));
        Buttons[0].OnClicked.AddListener(() => AdjustSlider(0,true));
        Buttons[1].OnClicked.AddListener(() => AdjustSlider(0, false));
        Buttons[2].OnClicked.AddListener(() => Leave());
        Buttons[3].OnClicked.AddListener(() => Delete());
        Buttons[4].OnClicked.AddListener(() => Undo());
        Buttons[5].OnClicked.AddListener(() => Redo());
        //Buttons[2].IsToggled = false;
        config.VRShaderType = Config.ShaderType.FixedThreshold;

        sliders[0].Value = config.ViewThresh / (float)SLIDER_MAXIMUM[0];
    }

    private void Redo()
    {
        config.invoker.Redo();
    }

    private void Undo()
    {
        config.invoker.Undo();
    }

    private void Delete()
    {
        config.invoker.Execute(new DeleteCommand(config.invoker, config.tracer, config._curIndex));
    }

    void UpdateViewValue(SliderEventData data)
    {
        int value = Mathf.RoundToInt(data.NewValue * SLIDER_MAXIMUM[0]);
        value = Mathf.Clamp(value, 5, 128);

        TextMeshProUGUI[] textMeshProUGUIs = GetComponentsInChildren<TextMeshProUGUI>();
        var viewText = textMeshProUGUIs[0];
        viewText.text = $"View Threshold:\n {value}";

        config.ViewThresh = value;

        config.VRShaderType = Config.ShaderType.FixedThreshold;
        config._postProcessVolume.profile.GetSetting<BaseVolumeRendering>().viewThreshold.overrideState = true;
        config._postProcessVolume.profile.GetSetting<BaseVolumeRendering>().mask.overrideState = true;
        config._postProcessVolume.profile.GetSetting<BaseVolumeRendering>().mask.value = config.tracer.fim.mask;
        config._postProcessVolume.profile.GetSetting<BaseVolumeRendering>().viewThreshold.value = value/255.0f;
    }

    void Leave()
    {
        Hide();
    }

    void AdjustSlider(int index, bool up)
    {
        if (up)
            sliders[index].Value += 1.0f / SLIDER_MAXIMUM[index];
        else
            sliders[index].Value -= 1.0f / SLIDER_MAXIMUM[index];
    }

    public override void Hide()
    {
        config.VRShaderType = Config.ShaderType.Base;
        config.gazeController.interactionType = GazeController.EyeInteractionType.None;
        mainMenu.buttons[3].ForceSetToggled(false);
        base.Hide();
    }

    public override void Show()
    {
        config.VRShaderType = Config.ShaderType.FixedThreshold;
        base.Show();
    }
}
