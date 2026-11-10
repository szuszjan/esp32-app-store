Imports System.Diagnostics
Imports System.IO
Imports System.IO.Compression
Imports System.IO.Ports
Imports System.Management
Imports System.Text.RegularExpressions

Public Class PortInfo
    Public Property Name As String = ""
    Public Property Description As String = ""
    Public Property IsEspressif As Boolean
    Public Overrides Function ToString() As String
        Return If(Description = "", Name, $"{Name}  -  {Description}")
    End Function
End Class

Public Module Ports
    ' COM ports with their device names (WMI), Espressif / USB-serial chips first.
    Public Function List() As List(Of PortInfo)
        Dim names = SerialPort.GetPortNames().Distinct().ToList()
        Dim result As New List(Of PortInfo)
        Dim desc As New Dictionary(Of String, (String, Boolean))(StringComparer.OrdinalIgnoreCase)
        Try
            Using q As New ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'")
                For Each o As ManagementObject In q.Get()
                    Dim n = CStr(o("Name")), id = CStr(If(o("PNPDeviceID"), ""))
                    Dim m = Regex.Match(n, "\((COM\d+)\)")
                    If m.Success Then
                        Dim esp = id.Contains("VID_303A") OrElse id.Contains("VID_10C4") OrElse id.Contains("VID_1A86") OrElse id.Contains("VID_0403")
                        desc(m.Groups(1).Value) = (n.Replace("(" & m.Groups(1).Value & ")", "").Trim(), esp)
                    End If
                Next
            End Using
        Catch
        End Try
        For Each n In names
            Dim p As New PortInfo With {.Name = n}
            If desc.ContainsKey(n) Then p.Description = desc(n).Item1 : p.IsEspressif = desc(n).Item2
            result.Add(p)
        Next
        Return result.OrderByDescending(Function(p) p.IsEspressif).ThenBy(Function(p) p.Name.Length).ThenBy(Function(p) p.Name).ToList()
    End Function

    ' TinyUSB firmwares (e.g. DuskOS) jump into the ROM bootloader when the port is opened at 1200
    ' baud and closed; the board then comes back as a new COM port. Returns that port, or the same
    ' one if nothing new showed up within the timeout.
    Public Async Function Touch1200Async(port As String, log As Action(Of String)) As Task(Of String)
        Dim before = SerialPort.GetPortNames().ToHashSet(StringComparer.OrdinalIgnoreCase)
        log($"1200 bps reset on {port}")
        Try
            Using sp As New SerialPort(port, 1200)
                sp.DtrEnable = True
                sp.Open()
                Await Task.Delay(120)
                sp.DtrEnable = False
                sp.Close()
            End Using
        Catch ex As Exception
            log("  (" & ex.Message & ")")
        End Try
        Dim t0 = DateTime.Now
        While (DateTime.Now - t0).TotalSeconds < 8
            Await Task.Delay(250)
            Dim now = SerialPort.GetPortNames()
            Dim added = now.FirstOrDefault(Function(n) Not before.Contains(n))
            If added IsNot Nothing Then
                log($"  bootloader appeared on {added}")
                Await Task.Delay(500)
                Return added
            End If
            If Not now.Contains(port, StringComparer.OrdinalIgnoreCase) Then before.Remove(port)   ' it left; wait for it to come back
        End While
        Return port
    End Function
End Module

Public Module Esptool
    Public Const PinnedVersion As String = "v4.13.0"
    Private ReadOnly PinnedUrl As String = $"https://github.com/espressif/esptool/releases/download/{PinnedVersion}/esptool-{PinnedVersion}-windows-amd64.zip"

    ' esptool.exe: next to this program, then our own download, then Espressif's official release
    ' (downloaded once), and the Arduino ESP32 core's copy as a last resort.
    Public Async Function LocateAsync(log As Action(Of String)) As Task(Of String)
        Dim here = Path.Combine(AppContext.BaseDirectory, "esptool.exe")
        If File.Exists(here) Then Return here
        Dim dir = Path.Combine(CatalogIO.DataDir, "esptool-" & PinnedVersion)
        Dim found = If(Directory.Exists(dir), Directory.GetFiles(dir, "esptool.exe", SearchOption.AllDirectories).FirstOrDefault(), Nothing)
        If found IsNot Nothing Then Return found
        Try
            log($"Downloading esptool {PinnedVersion} from Espressif (GitHub)")
            Dim zip = Path.Combine(CatalogIO.DataDir, "esptool.zip")
            Dim bytes = Await CatalogIO.Http.GetByteArrayAsync(PinnedUrl)
            File.WriteAllBytes(zip, bytes)
            If Directory.Exists(dir) Then Directory.Delete(dir, True)
            ZipFile.ExtractToDirectory(zip, dir)
            File.Delete(zip)
            found = Directory.GetFiles(dir, "esptool.exe", SearchOption.AllDirectories).FirstOrDefault()
            If found IsNot Nothing Then Return found
        Catch ex As Exception
            log("  esptool download failed: " & ex.Message)
        End Try
        Dim ard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arduino15", "packages", "esp32", "tools", "esptool_py")
        If Directory.Exists(ard) Then
            found = Directory.GetFiles(ard, "esptool.exe", SearchOption.AllDirectories).OrderByDescending(Function(f) f).FirstOrDefault()
            If found IsNot Nothing Then log("Using the Arduino core's esptool: " & found) : Return found
        End If
        Throw New FileNotFoundException("esptool.exe not found and could not be downloaded")
    End Function

    ' Runs esptool, streaming its output. onPercent gets the percentage of the current "Writing at"
    ' line together with the flash address it is at. Returns the exit code.
    Public Async Function RunAsync(exe As String, args As String, log As Action(Of String), onWrite As Action(Of Long, Double),
                                   Optional cancel As Threading.CancellationToken = Nothing) As Task(Of Integer)
        Dim psi As New ProcessStartInfo(exe, args) With {
            .UseShellExecute = False, .RedirectStandardOutput = True, .RedirectStandardError = True, .CreateNoWindow = True}
        log("> esptool " & args)
        Using p As New Process With {.StartInfo = psi, .EnableRaisingEvents = True}
            p.Start()
            Dim rxWrite As New Regex("Writing at 0x([0-9a-fA-F]+)[^\d(]*(?:\(\s*(\d+)\s*%\)|.*?(\d+(?:\.\d+)?)\s*%)")
            Dim pump = Async Function(reader As StreamReader) As Task
                           ' esptool redraws progress with \r: split on both
                           Dim sb As New Text.StringBuilder(), buf(1023) As Char
                           Do
                               Dim n = Await reader.ReadAsync(buf, 0, buf.Length)
                               If n <= 0 Then Exit Do
                               For i = 0 To n - 1
                                   Dim c = buf(i)
                                   If c = vbCr OrElse c = vbLf Then
                                       If sb.Length > 0 Then
                                           Dim line = sb.ToString() : sb.Clear()
                                           Dim m = rxWrite.Match(line)
                                           If m.Success Then
                                               Dim pct = Double.Parse(If(m.Groups(2).Success, m.Groups(2).Value, m.Groups(3).Value), Globalization.CultureInfo.InvariantCulture)
                                               onWrite(Convert.ToInt64(m.Groups(1).Value, 16), pct)
                                           Else
                                               log(line)
                                           End If
                                       End If
                                   Else
                                       sb.Append(c)
                                   End If
                               Next
                           Loop
                           If sb.Length > 0 Then log(sb.ToString())
                       End Function
            Dim t1 = pump(p.StandardOutput), t2 = pump(p.StandardError)
            Using reg = cancel.Register(Sub()
                                            Try
                                                If Not p.HasExited Then p.Kill(True)
                                            Catch
                                            End Try
                                        End Sub)
                Await Task.WhenAll(t1, t2)
                Await p.WaitForExitAsync()
            End Using
            Return p.ExitCode
        End Using
    End Function
End Module
