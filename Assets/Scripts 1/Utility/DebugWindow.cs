//using TMPro;
//using UnityEngine;
//using UnityEngine.UI;

//namespace MRTK.Tutorials.AzureSpatialAnchors
//{
//    public class DebugWindow : MonoBehaviour
//    {
//        [SerializeField] private TextMeshProUGUI debugText = default;

//        private ScrollRect scrollRect;
//        string text;
//        bool needUpdate = false;

//        private void Start()
//        {
//            // Cache references
//            scrollRect = GetComponentInChildren<ScrollRect>();

//            // Subscribe to log message events

//            //Application.logMessageReceived += HandleLog;
//            Application.logMessageReceivedThreaded += HandleLog;

//            // Set the starting text
//            debugText.text = "Debug messages will appear here.\n\n";
//        }

//        private void Update()
//        {
//            if (needUpdate)
//            {
//                debugText.text += text;
//                Canvas.ForceUpdateCanvases();
//                scrollRect.verticalNormalizedPosition = 0;
//                needUpdate = false;
//                text = "";
//            }
//        }

//        private void OnDestroy()
//        {
//            Application.logMessageReceived -= HandleLog;
//        }

//        private void HandleLog(string message, string stackTrace, LogType type)
//        {
//            text += message + " \n";
//            needUpdate = true;
//        }
//    }
//}
