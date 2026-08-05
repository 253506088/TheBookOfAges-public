namespace TheBookOfAges.TheBookOfAgesCode.Services;

/// <summary>
/// GM 诊断会话服务：用一个很小的本地标记文件判断上次游戏是否正常退出。
/// </summary>
public sealed class GmDiagnosticSessionService
{
    private const string RunningMarker = "running";
    private const string ClosedMarker = "closed";
    private readonly string _stateFilePath;

    /// <summary>
    /// 创建诊断会话服务。
    /// </summary>
    public GmDiagnosticSessionService(string userDataDirectory)
    {
        string diagnosticDirectory = Path.Combine(userDataDirectory, "TheBookOfAges", "diagnostics");
        _stateFilePath = Path.Combine(diagnosticDirectory, "diagnostic_session_state.txt");
    }

    /// <summary>
    /// 标记本次会话开始，并返回上次会话是否疑似没有正常结束。
    /// </summary>
    public bool MarkSessionStarted()
    {
        bool previousSessionWasRunning = TryReadState().StartsWith(RunningMarker, StringComparison.OrdinalIgnoreCase);
        TryWriteState($"{RunningMarker}|{DateTimeOffset.Now:O}");
        return previousSessionWasRunning;
    }

    /// <summary>
    /// 标记本次会话正常结束。
    /// </summary>
    public void MarkSessionClosed()
    {
        TryWriteState($"{ClosedMarker}|{DateTimeOffset.Now:O}");
    }

    /// <summary>
    /// 尝试读取上次会话标记，读取失败时返回空字符串。
    /// </summary>
    private string TryReadState()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                return string.Empty;
            }

            return File.ReadAllText(_stateFilePath);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 尝试写入会话标记，写入失败时不影响游戏运行。
    /// </summary>
    private void TryWriteState(string state)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_stateFilePath, state);
        }
        catch
        {
            // 诊断标记只是兜底辅助，写失败不能影响游戏启动。
        }
    }
}
