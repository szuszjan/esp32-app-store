Imports System.Text

' The install itself, shared by the window and the command line (--install).
Public Class InstallResult
    Public Property Ok As Boolean
    Public Property ExitCode As Integer
    Public Property Port As String = ""
    Public Property Output As String = ""
    Public ReadOnly Property NeedsDownloadMode As Boolean
        Get
            Return Output.Contains("Failed to connect") OrElse Output.Contains("No serial data received") OrElse Output.Contains("Wrong boot mode")
        End Get
    End Property
    Public ReadOnly Property PortBusy As Boolean
        Get
            Return Output.Contains("could not open port") OrElse Output.Contains("PermissionError") OrElse Output.Contains("Access is denied")
        End Get
    End Property
End Class

Public Module Installer
    ' progress: 0..1 overall (0-0.1 download, 0.1-1 writing); status: a short line for the UI.
    Public Async Function InstallAsync(app As AppEntry, port As String, eraseAll As Boolean, log As Action(Of String),
                                       progress As Action(Of Double), status As Action(Of String)) As Task(Of InstallResult)
        status("Getting esptool...")
        Dim exe = Await Esptool.LocateAsync(log)
        log("esptool: " & exe)
        status($"Downloading {app.Name}...")
        Dim paths = Await CatalogIO.FetchFilesAsync(app, Sub(d, t) progress(0.1 * d / t), log)
        Dim files = app.Files.Select(Function(f, i) (f, paths(i))).OrderBy(Function(x) x.f.OffsetValue).ToList()
        Dim total = Math.Max(1L, files.Sum(Function(x) x.f.Size))

        If app.Reset = "1200bps" Then port = Await Ports.Touch1200Async(port, log)
        Dim baseArgs = $"--chip {app.Chip} --port {port} --baud {app.Baud} --before default_reset --after hard_reset"
        Dim output As New StringBuilder()
        Dim logBoth As Action(Of String) = Sub(s)
                                               SyncLock output : output.AppendLine(s) : End SyncLock
                                               log(s)
                                           End Sub
        Dim rc = 0
        If eraseAll Then
            status("Erasing flash (can take ~30 s)...")
            rc = Await Esptool.RunAsync(exe, baseArgs & " erase_flash", logBoth, Sub(a, p) Return)
        End If
        If rc = 0 Then
            status($"Writing {app.Name} to {port}...")
            Dim args = baseArgs & " write_flash -z --flash_mode keep --flash_freq keep --flash_size keep " &
                       String.Join(" ", files.Select(Function(x) $"{x.f.Offset} ""{x.Item2}"""))
            rc = Await Esptool.RunAsync(exe, args, logBoth,
                Sub(addr, pct)
                    Dim cur = files(0).f
                    For Each x In files
                        If x.f.OffsetValue <= addr Then cur = x.f
                    Next
                    Dim before = files.Where(Function(x) x.f.OffsetValue < cur.OffsetValue).Sum(Function(x) x.f.Size)
                    progress(0.1 + 0.9 * (before + cur.Size * pct / 100.0) / total)
                    status($"Writing {cur.Name}   {pct:0}%")
                End Sub)
        End If
        If rc = 0 Then progress(1)
        Return New InstallResult With {.Ok = rc = 0, .ExitCode = rc, .Port = port, .Output = output.ToString()}
    End Function

    ' ESP32AppStore.exe --install <catalog> <app-id> <COMx> [--erase]  (log next to the exe in install.log)
    Public Function RunCommandLine(args As String()) As Integer
        Dim logPath = IO.Path.Combine(CatalogIO.DataDir, "install.log")
        Using w As New IO.StreamWriter(logPath, False) With {.AutoFlush = True}
            Dim log As Action(Of String) = Sub(s)
                                               SyncLock w : w.WriteLine(s) : End SyncLock
                                           End Sub
            Try
                If args.Length < 4 Then log("usage: --install <catalog> <app-id> <COMx> [--erase]") : Return 2
                Dim doc = CatalogIO.LoadAsync(args(1)).GetAwaiter().GetResult()
                Dim app = doc.Apps.FirstOrDefault(Function(a) a.Id = args(2))
                If app Is Nothing Then log("no app " & args(2)) : Return 2
                Dim lastPct = -1
                Dim r = InstallAsync(app, args(3), args.Contains("--erase"), log,
                                     Sub(p)
                                         Dim v = CInt(p * 100)
                                         If v \ 10 <> lastPct \ 10 Then lastPct = v : log($"PROGRESS {v}%")
                                     End Sub,
                                     Sub(s) Return).GetAwaiter().GetResult()
                log(If(r.Ok, $"RESULT OK {app.Id} {app.Version} {r.Port}", $"RESULT FAIL {r.ExitCode} downloadmode={r.NeedsDownloadMode} busy={r.PortBusy}"))
                Return If(r.Ok, 0, 1)
            Catch ex As Exception
                log("RESULT ERROR " & ex.ToString())
                Return 3
            End Try
        End Using
    End Function
End Module
