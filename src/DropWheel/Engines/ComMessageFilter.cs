using System.Runtime.InteropServices;

namespace DropWheel.AppEngines;

/// <summary>
/// Фильтр сообщений COM для потока Office. Занятый Office отвечает «вызов отклонён»
/// (RPC_E_CALL_REJECTED 0x80010001) или «повторите позже» (RPC_E_SERVERCALL_RETRYLATER 0x8001010A) —
/// с фильтром COM сам повторяет любой вызов через 250 мс, до 30 с; дальше вызов падает с ошибкой,
/// а зависание целиком ловит таймаут задачи (OfficeEngine). Работает только в STA-потоке.
/// Это стандартный приём из документации Visual Studio («Call was rejected by callee»).
/// </summary>
[ComVisible(true)]
public sealed class ComMessageFilter : IOleMessageFilter
{
    private const int ServerCallIsHandled = 0, ServerCallRejected = 1, ServerCallRetryLater = 2;
    private const int PendingMsgWaitDefProcess = 2;
    private const int RetryDelayMs = 250, RetryForMs = 30_000;

    /// <summary>Включить фильтр для текущего (STA) потока. Ошибка — не повод падать: просто без повторов.</summary>
    public static void Register(Action<string> log)
    {
        try
        {
            var hr = CoRegisterMessageFilter(new ComMessageFilter(), out _);
            if (hr != 0) log($"Office: фильтр сообщений COM не включён (0x{hr:X8})");
        }
        catch (Exception ex)
        {
            log("Office: фильтр сообщений COM не включён — " + ex.Message);
        }
    }

    public static void Revoke()
    {
        try { CoRegisterMessageFilter(null, out _); }
        catch { /* выход из потока — не важно */ }
    }

    int IOleMessageFilter.HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo) =>
        ServerCallIsHandled;

    int IOleMessageFilter.RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType) =>
        dwRejectType is ServerCallRejected or ServerCallRetryLater && dwTickCount < RetryForMs
            ? RetryDelayMs // ≥ 100 — подождать столько миллисекунд и повторить
            : -1;          // отказаться: вызов вернёт ошибку

    int IOleMessageFilter.MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType) =>
        PendingMsgWaitDefProcess;

    [DllImport("ole32.dll")]
    private static extern int CoRegisterMessageFilter(IOleMessageFilter? newFilter, out IOleMessageFilter? oldFilter);
}

[ComImport, Guid("00000016-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IOleMessageFilter
{
    [PreserveSig] int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);
    [PreserveSig] int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);
    [PreserveSig] int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
}
