using MonitorLib.GOT;
using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Profiling;
using UnityEngine.UI;

public class GOTProfiler : MonoBehaviour
{
    [Header("是否监控日志")]
    public bool EnableLog = false;
    [Header("是否采集帧图")]
    public bool EnableFrameTexture = false;
#if ENABLE_ANALYSIS
    [Header("是否采集函数性能")]
    public bool EnableFunctionAnalysis = false;
#endif
    public Text UploadTips;
    public Text ReportUrl;
    string btnMsg = "开始监控";
    bool btnMonitor = false;

    Action<bool> MonitorCallback;

    private int m_FPS = 0;

    private int m_TickTime = 0;
    private string m_StartTime;
#if ENABLE_ANALYSIS
    //函数性能分析地址
    private string funcAnalysisFilePath;
#endif
    //log日志路径
    private string logFilePath;
    //设备信息路径
    private string deviceFilePath;
    //测试信息路径
    private string testFilePath;
    //性能监控
    private string monitorFilePath;

    private int m_frameIndex = 0;

    private int m_IgnoreFrameCount = 10;

    MonitorInfos monitorInfos = null;

    private float m_Accumulator = 0;
    private int m_Frames = 0;
    private float m_TimeLeft;
    private float m_UpdateInterval = 0.5f;

    void Awake()
    {
        Application.targetFrameRate = 60;
    }

    void Start()
    {
        GameObject.DontDestroyOnLoad(gameObject);
        MonitorCallback += (res) =>
        {
            if (res)
            {
                Debug.Log(Config.Monitoring);
                m_frameIndex = 0;
                ShareDatas.StartTime = DateTime.Now;
                m_StartTime = ShareDatas.StartTime.ToString().Replace(" ", "_").Replace("/", "_").Replace(":", "_");
                ShareDatas.StartTimeStr = m_StartTime;

                if (EnableFrameTexture)
                {
                    CreateDir();
                }
#if ENABLE_ANALYSIS
                if (EnableFunctionAnalysis)
                    funcAnalysisFilePath = $"{Application.persistentDataPath}/funcAnalysis_{m_StartTime}.txt";
#endif
                logFilePath = $"{Application.persistentDataPath}/log_{m_StartTime}.txt";
                deviceFilePath = $"{Application.persistentDataPath}/device_{m_StartTime}.txt";
                testFilePath = $"{Application.persistentDataPath}/test_{m_StartTime}.txt";
                monitorFilePath = $"{Application.persistentDataPath}/monitor_{m_StartTime}.txt";
                if (EnableLog)
                {
                    LogManager.CreateLogFile(logFilePath, System.IO.FileMode.Append);
                    Application.logMessageReceived += LogManager.LogToFile;
                }

                m_TickTime = 0;
                InvokeRepeating("Tick", 1.0f, 1.0f);

                GetSystemInfo();

                if (ReportUrl != null)
                {
                    ReportUrl.gameObject.SetActive(false);
                }

                //测试Log颜色
                Debug.LogError("测试Error Log");
                Debug.LogWarning("测试Warning Log");

                StartMonitor();
            }
            else
            {
                ReportFunctionAnalysis();
                Debug.Log(Config.MonitorStop);
                ShareDatas.EndTime = DateTime.Now;
                string testTime = ShareDatas.GetTestTime();
                //上传测试时间
                FileManager.WriteToFile(testFilePath, $"应用名：{Application.productName}&nbsp&nbsp&nbsp包名：{Application.identifier}&nbsp&nbsp&nbsp测试系统：{Application.platform}&nbsp&nbsp&nbsp版本号：{Application.version}&nbsp&nbsp&nbsp本次测试时长:{testTime}");
                UploadFile(testFilePath);

                CancelInvoke("Tick");
                m_TickTime = 0;

                if (EnableLog)
                {
                    Application.logMessageReceived -= LogManager.LogToFile;
                    LogManager.CloseLogFile();
                }
                if (EnableLog)
                {
                    FileManager.ReplaceContent(logFilePath, "[Log]", "<font color=\"#0000FF\">[Log]</font>");
                    FileManager.ReplaceContent(logFilePath, "[Error]", "<font color=\"#FF0000\">[Error]</font>");
                    FileManager.ReplaceContent(logFilePath, "[Warning]", "<font color=\"#FFD700\">[Warning]</font>");
                    UploadFile(logFilePath);
                }
#if ENABLE_ANALYSIS
                if (EnableFunctionAnalysis)
                    HookUtil.PrintProfilerDatas();
#endif
                StopMonitor();

                if (ReportUrl != null)
                {
                    UploadReportHtml(m_StartTime);
                    ReportUrl.gameObject.SetActive(true);
                    var url = string.Format(ShareDatas.ReportUrl, m_StartTime);
                    ReportUrl.text = $"<a href={url}>[{url}]</a>";
                }
            }
        };

        StartCoroutine(DownloadReportTemplete());
    }

    [HideAnalysis] //暂时这个函数有问题，统计实行时间是负数
    void ReportFunctionAnalysis()
    {
        //函数性能监控
#if ENABLE_ANALYSIS
        if (EnableFunctionAnalysis)
        {
            StringBuilder sb = new StringBuilder();
            var datas = HookUtil.GetFunctionMonitorFileDatas();
            if (datas != null && datas.Count > 0)
            {
                Debug.Log("--------输出所有函数的性能数据-------");
                foreach (var data in datas)
                {
                    Debug.Log(data);
                    sb.Append(data);
                    sb.Append("\n");
                }
                //FunctionAnalysisDatas funcAnalysisData = new FunctionAnalysisDatas();
                //funcAnalysisData.FunctionAnalysDatas = new System.Collections.Generic.List<FunctionMonitorFileDatas>();
                //funcAnalysisData.FunctionAnalysDatas.AddRange(datas);
                //var datasJsonStr = JsonUtility.ToJson(funcAnalysisData);
                Debug.Log("*****显示函数性能监控列表*****");
                //Debug.Log(datasJsonStr);
                string dataStr = sb.ToString();
                EmailManager.Send(dataStr);
                var funcAnalysisFile = FileManager.WriteToFile(funcAnalysisFilePath, dataStr);
                if (funcAnalysisFile)
                {
                    UploadFile(funcAnalysisFilePath);
                }
            }
            else
            {
                Debug.Log("--------没有函数性能监控数据---------");
            }
        }
#endif
    }

    void CreateDir()
    {
        var dirPath = $"{Application.persistentDataPath}/{m_StartTime}/";
        if (Directory.Exists(dirPath))
        {
            Directory.Delete(dirPath, true);
        }
        Directory.CreateDirectory(dirPath);
    }

    System.Collections.IEnumerator DownloadReportTemplete()
    {
        var url = "";
#if ENABLE_ANALYSIS
        if (EnableFunctionAnalysis && EnableLog)
        {
            url = $"http://{Config.IP}/ReportTempleteWithFuncAnalysis.html";
        }
        else if (!EnableLog && !EnableFunctionAnalysis)
        {
            url = $"http://{Config.IP}/ReportTempleteWithoutLog.html";
        }
        else
        {
            Debug.LogError("在ENABLE_ANALYSIS的宏定义下，EnableLog和EnableFunctionAnalysis必须要同时打开或者关闭");
        }
#else
        if (!EnableLog)
            url = $"http://{Config.IP}/ReportTempleteWithoutLog.html";
        else
            url = $"http://{Config.IP}/ReportTemplete.html";
#endif
        using (var webRequest = UnityWebRequest.Get(url))
        {
            yield return webRequest.SendWebRequest();
#if UNITY_2020
            if (webRequest.result == UnityWebRequest.Result.ConnectionError)
#else
            if (webRequest.isHttpError)
#endif
            {
                Debug.LogError(webRequest.error);
            }
            else
            {
                var fileHandler = webRequest.downloadHandler;
                var templetePath = "";
#if ENABLE_ANALYSIS
                if (EnableFunctionAnalysis && EnableLog)
                {
                    templetePath = $"{Application.persistentDataPath}/ReportTempleteWithFuncAnalysis.html";
                }
                else if (!EnableLog && !EnableFunctionAnalysis)
                {
                    templetePath = $"{Application.persistentDataPath}/ReportTempleteWithoutLog.html";
                }
                else
                {
                    Debug.LogError("在ENABLE_ANALYSIS的宏定义下，EnableLog和EnableFunctionAnalysis必须要同时打开或者关闭");
                }
#else
                if (EnableLog)
                    templetePath = $"{Application.persistentDataPath}/ReportTemplete.html";
                else
                    templetePath = $"{Application.persistentDataPath}/ReportTempleteWithoutLog.html";
#endif

                if (FileManager.WriteBytesToFile(templetePath, fileHandler.data))
                {
                    Debug.Log("模板文件下载成功");
                }
                else
                {
                    Debug.LogError("模板文件下载失败");
                }
            }
        }
        yield return null;
    }

    void StartMonitor()
    {
        monitorInfos = new MonitorInfos();
    }

    void StopMonitor()
    {
        if (monitorInfos.MonitorInfoList.Count > 1)
        {
            monitorInfos.MonitorInfoList.RemoveAt(monitorInfos.MonitorInfoList.Count - 1);
        }

        var monitorToFile = FileManager.WriteToFile(monitorFilePath, JsonUtility.ToJson(monitorInfos));
        if (monitorToFile)
        {
            UploadFile(monitorFilePath);
        }
    }

    void UploadReportHtml(string time)
    {
        var newPath = Application.persistentDataPath + $"/report_{time}.html";
#if ENABLE_ANALYSIS
        if (EnableFunctionAnalysis && EnableLog)
        {
            File.Copy(Application.persistentDataPath + "/ReportTempleteWithFuncAnalysis.html", newPath, true);
        }
        else if (!EnableLog && !EnableFunctionAnalysis)
        {
            File.Copy(Application.persistentDataPath + "/ReportTempleteWithoutLog.html", newPath, true);
        }
        else
        {
            Debug.LogError("在ENABLE_ANALYSIS的宏定义下，EnableLog和EnableFunctionAnalysis必须要同时打开或者关闭");
        }
#else
        if (EnableLog)
            File.Copy(Application.persistentDataPath + "/ReportTemplete.html", newPath, true);
        else
            File.Copy(Application.persistentDataPath + "/ReportTempleteWithoutLog.html", newPath, true);
#endif
        if (EnableLog)
            FileManager.ReplaceContent(newPath, $"{time}", "{0}", "{1}", "{2}", "{3}", "{4}","{5}");
        else
            FileManager.ReplaceContent(newPath, $"{time}", "{0}", "{1}", "{3}", "{4}","{5}");
        UploadFile(newPath);
    }

    void UploadFile(string filePath)
    {
        FileUploadManager.UploadFile(filePath, (sender, e) =>
        {
            Debug.Log("Uploading Progreess: " + e.ProgressPercentage);
            if (e.ProgressPercentage > 0 && e.ProgressPercentage < 100)
            {
                if (UploadTips != null && !UploadTips.gameObject.activeSelf)
                {
                    UploadTips.gameObject.SetActive(true);
                }
            }
            else if (e.ProgressPercentage >= 100)
            {
                if (UploadTips != null && UploadTips.gameObject.activeSelf)
                {
                    UploadTips.gameObject.SetActive(false);
                }
            }
            UploadTips.text = $"数据上传中,进度{e.ProgressPercentage}%";
        }, (sender, e) =>
        {
            Debug.Log($"File Uploaded :{e.Result}");
        });
    }

    void Tick()
    {
        m_TickTime++;
    }

    void GetSystemInfo()
    {
        DeviceInfo deviceInfo = new DeviceInfo()
        {
            UnityVersion = Application.unityVersion,
            DeviceModel = SystemInfo.deviceModel,
            BatteryLevel = SystemInfo.batteryLevel,
            DeviceName = SystemInfo.deviceName,
            DeviceUniqueIdentifier = SystemInfo.deviceUniqueIdentifier,
            GraphicsDeviceName = SystemInfo.graphicsDeviceName,
            GraphicsDeviceVendor = SystemInfo.graphicsDeviceVendor,
            GraphicsDeviceVersion = SystemInfo.graphicsDeviceVersion,
            GraphicsMemorySize = SystemInfo.graphicsMemorySize,
            OperatingSystem = SystemInfo.operatingSystem,
            ProcessorCount = SystemInfo.processorCount,
            ProcessorFrequency = SystemInfo.processorFrequency,
            ProcessorType = SystemInfo.processorType,
            SupportsShadows = SystemInfo.supportsShadows,
            SystemMemorySize = SystemInfo.systemMemorySize,
            ScreenHeight = Screen.height,
            ScreenWidth = Screen.width
        };

        var deviceInfoToFile = FileManager.WriteToFile(deviceFilePath, JsonUtility.ToJson(deviceInfo));
        if (deviceInfoToFile)
        {
            UploadFile(deviceFilePath);
        }
    }

    [HideAnalysis]
    void OnGUI()
    {
        if (GUI.Button(new Rect(150, 350, 200, 100), btnMsg))
        {
            btnMonitor = !btnMonitor;
            btnMsg = btnMonitor ? Config.Monitoring : Config.MonitorBegin;
            if (MonitorCallback != null)
                MonitorCallback.Invoke(btnMonitor);
        }
        if (btnMonitor)
            btnMsg = $"{Config.Monitoring}{m_TickTime}s";
        GUI.Label(new Rect(Screen.width / 2, 0, 100, 100), "FPS:" + m_FPS);
    }

    [HideAnalysis]
    void Update()
    {
        m_Frames++;
        m_Accumulator += Time.unscaledDeltaTime;
        m_TimeLeft -= Time.unscaledDeltaTime;
        if (m_TimeLeft <= 0f)
        {
            m_FPS = (int)(m_Accumulator > 0f ? m_Frames / m_Accumulator : 0f);
            m_Frames = 0;
            m_Accumulator = 0f;
            m_TimeLeft += m_UpdateInterval;
        }

        if (btnMonitor)
        {
            ++m_frameIndex;
            if (m_frameIndex > m_IgnoreFrameCount)
            {
                var monitorInfo = new MonitorInfo() { FrameIndex = m_frameIndex - m_IgnoreFrameCount, BatteryLevel = SystemInfo.batteryLevel, MemorySize = 0, Frame = m_FPS, MonoHeapSize = Profiler.GetMonoHeapSizeLong(), MonoUsedSize = Profiler.GetMonoUsedSizeLong(), TotalAllocatedMemory = Profiler.GetTotalAllocatedMemoryLong(), TotalUnusedReservedMemory = Profiler.GetTotalUnusedReservedMemoryLong(), UnityTotalReservedMemory = Profiler.GetTotalReservedMemoryLong(), AllocatedMemoryForGraphicsDriver = Profiler.GetAllocatedMemoryForGraphicsDriver() };
                monitorInfos.MonitorInfoList.Add(monitorInfo);
                if (EnableFrameTexture)
                {
                    ScreenCapture.CaptureScreenshot($"{Application.persistentDataPath}/{m_StartTime}/img_{m_StartTime}_{m_frameIndex - m_IgnoreFrameCount}.png");
                }
            }
        }
    }

    void OnDestroy()
    {
        LogManager.CloseLogFile();
    }
}
