using Microsoft.MixedReality.Toolkit;
using Microsoft.MixedReality.Toolkit.SpatialManipulation;
using Microsoft.MixedReality.Toolkit.UX;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class BlockerMenu : SubMenu
{
    Config config;
    Dictionary<PressableButton,Blocker> buttonBlockers = new();
    Dictionary<PressableButton,Blocker> blockerButtons = new();

    List<StatefulInteractable> toggles = new();

    public ToggleCollection toggleCollection;

    // Start is called before the first frame update
    void Start()
    {
        config = GameObject.FindGameObjectWithTag("Config").GetComponent<Config>();
        var Buttons = GetComponentsInChildren<PressableButton>();

        Buttons[1].OnClicked.AddListener(() => OnNewClicked());
        Buttons[2].OnClicked.AddListener(() => OnDeleteClicked());
        Buttons[3].OnClicked.AddListener(() => OnCloseClicked());


        foreach(Blocker blocker in config._blockers)
        {
            PressableButton button = InitializeBlocker(blocker);
        }

        toggleCollection.OnToggleSelected.AddListener((index) => { OnToggleSeclected(index); });
    }

    public override void Hide()
    {
        base.Hide();
        foreach (var button in toggleCollection.Toggles)
        {
            buttonBlockers[button as PressableButton].GetComponent<MeshRenderer>().material.color = new Color(0.4874213f, 0.7125539f, 1f, 0.3764706f);
        }
    }

    private void OnToggleSeclected(int index)
    {
        foreach(var button in toggleCollection.Toggles)
        {
            var blocker = buttonBlockers[button as PressableButton];
            if (toggleCollection.Toggles.IndexOf(button) == index)
            {
                blocker.GetComponent<MeshRenderer>().material.color = new Color(0.5149505f, 1.0f, 0.4862745f, 0.3764706f);
                blocker.GetComponent<ObjectManipulator>().enabled = true;

            }
            else
            {
                blocker.GetComponent<MeshRenderer>().material.color = new Color(0.4874213f, 0.7125539f, 1f, 0.3764706f);
                blocker.GetComponent<ObjectManipulator>().enabled = false;
            }
        }
    }

    private void OnBlockerClicked(PressableButton button)
    {
        if (!button.IsToggled)
        {
            buttonBlockers[button].GetComponent<MeshRenderer>().material.color = new Color(0.4874213f, 0.7125539f, 1f, 0.3764706f);
            config.paintingBoard.GetComponent<ObjectManipulator>().enabled = true;
        }
        else
        {
            buttonBlockers[button].GetComponent<MeshRenderer>().material.color = new Color(0.5149505f,1.0f,0.4862745f,0.3764706f);
            config.paintingBoard.GetComponent<ObjectManipulator>().enabled = false;
        }
    }

    private void OnCloseClicked()
    {
        Destroy(gameObject);
    }

    private void OnDeleteClicked()
    {
        var button = toggles[toggleCollection.CurrentIndex];
        toggles.Remove(button);
        toggleCollection.Toggles = new List<StatefulInteractable>(toggles);
        Blocker blocker = buttonBlockers[button as PressableButton];
        config._blockers.Remove(blocker);
        Destroy(blocker.gameObject);
        Destroy(button.gameObject);
    }

    void OnNewClicked()
    {
        GameObject blockerObj = GameObject.Instantiate(Resources.Load("Prefabs/Blocker") as GameObject);
        blockerObj.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 0.008f;
        blockerObj.transform.SetParent(GameObject.Find("Blockers").transform,false);

        Blocker blocker = blockerObj.GetComponent<Blocker>();
        config._blockers.Add(blocker);

        InitializeBlocker(blocker);

    }

    private PressableButton InitializeBlocker(Blocker blocker)
    {
        blocker.BMController = this;
        var parent = gameObject.transform.GetChild(0).GetChild(1);
        GameObject buttonObj = GameObject.Instantiate(Resources.Load("Prefabs/Action Button") as GameObject, parent);
        PressableButton button = buttonObj.GetComponent<PressableButton>();
        var textMeshProUGUI = buttonObj.GetComponentsInChildren<TextMeshProUGUI>();
        int index = config._blockers.IndexOf(blocker);
        textMeshProUGUI[1].text = $"Blocker {index}";
        buttonBlockers[button] = blocker;

        toggles.Add(button);
        toggleCollection.Toggles = new List<StatefulInteractable>(toggles);
        toggleCollection.SetSelection(index);

        button.OnClicked.AddListener(() => OnBlockerClicked(button));
        return button;
    }

    public void ActivateBlocker(Blocker blocker)
    {
        PressableButton button = new();
        foreach(var  pair  in buttonBlockers)
        {
            if (pair.Value == blocker)
            {
                button = pair.Key;
                break;
            }
        }
        int index = toggleCollection.Toggles.IndexOf(button);
        toggleCollection.SetSelection(index);
    }
}
