using MixedReality.Toolkit.UX;


public class IsolateMenu : SubMenu
{
    public PipeCasing pipeCasing;
    PressableButton[] buttons;

    // Start is called before the first frame update
    void Start()
    {
        buttons = GetComponentsInChildren<PressableButton>();
        buttons[0].OnClicked.AddListener(() => pipeCasing.Trace());
        buttons[1].OnClicked.AddListener(() => {
            pipeCasing.ClearPipes();
            gameObject.SetActive(false);
        }) ;
        buttons[2].OnClicked.AddListener(() => pipeCasing.AddLength());
        buttons[3].OnClicked.AddListener(() => pipeCasing.DecLength());
    }
}
